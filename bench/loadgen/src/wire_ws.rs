// Shared actual RFC6455 reader for plain and offered-deflate benchmark lanes.
use std::collections::BTreeMap;
use std::io::Read;
use std::sync::{Arc, Mutex};
use std::sync::atomic::{AtomicU64, Ordering};
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt, BufReader};
use serde_json::{Value, json};

type Res<T> = Result<T, Box<dyn std::error::Error + Send + Sync>>;
const MAX_HEADERS: usize = 16 * 1024;
const MAX_MESSAGE: usize = 16 * 1024 * 1024;
const OFFER: &str = "permessage-deflate; server_no_context_takeover; client_no_context_takeover; server_max_window_bits=15; client_max_window_bits=15";

#[derive(Default)]
pub struct Counters {
    bytes: AtomicU64,
    plain_data: AtomicU64,
    compressed_data: AtomicU64,
    control: AtomicU64,
    plain_connections: AtomicU64,
    deflate_connections: AtomicU64,
    parameters: Mutex<BTreeMap<String, u64>>,
}

#[derive(Clone, Copy)]
pub struct FrameSnapshot { bytes: u64, plain_data: u64, compressed_data: u64, control: u64 }

impl Counters {
    pub fn snapshot(&self) -> FrameSnapshot {
        let value = |counter: &AtomicU64| counter.load(Ordering::Relaxed);
        FrameSnapshot { bytes: value(&self.bytes), plain_data: value(&self.plain_data), compressed_data: value(&self.compressed_data), control: value(&self.control) }
    }
    pub fn negotiation_json(&self, requested: bool) -> Value {
        json!({ "requested_offer": if requested { "permessage-deflate" } else { "none" },
            "negotiated_plain": self.plain_connections.load(Ordering::Relaxed),
            "negotiated_deflate": self.deflate_connections.load(Ordering::Relaxed),
            "accepted_parameters": *self.parameters.lock().unwrap(),
            "scope": "observed successful HTTP/1.1 WebSocket upgrades, never inferred from offer" })
    }
}
impl FrameSnapshot {
    pub fn delta_json(self, before: Self) -> Value {
        json!({ "wire_bytes": self.bytes - before.bytes, "plain_data_frames": self.plain_data - before.plain_data,
            "compressed_data_frames": self.compressed_data - before.compressed_data, "control_frames": self.control - before.control,
            "scope": "received RFC6455 header and payload bytes consumed by the common reader; HTTP handshake excluded" })
    }
    pub fn byte_delta(self, before: Self) -> u64 { self.bytes - before.bytes }
}

pub struct WireWs<S> {
    reader: BufReader<S>,
    compressed: bool,
    counters: Arc<Counters>,
}

impl<S: AsyncRead + AsyncWrite + Unpin> WireWs<S> {
    pub async fn handshake(stream: S, address: &str, cookie: &str, requested: bool, agent: Option<&str>, counters: Arc<Counters>) -> Res<Self> {
        if [address, cookie].into_iter().any(|value| value.contains('\r') || value.contains('\n')) { return Err("invalid handshake header input".into()); }
        if agent.is_some_and(|value| value.is_empty() || !value.is_ascii() || value.contains('\r') || value.contains('\n')) { return Err("invalid User-Agent".into()); }
        let key = base64(&random_bytes::<16>()?);
        let extension = if requested { format!("Sec-WebSocket-Extensions: {OFFER}\r\n") } else { String::new() };
        let user_agent = agent.map(|value| format!("User-Agent: {value}\r\n")).unwrap_or_default();
        let request = format!("GET /cable HTTP/1.1\r\nHost: {address}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Protocol: actioncable-v1-json, actioncable-unsupported\r\n{extension}{user_agent}Origin: http://{address}\r\nCookie: {cookie}\r\n\r\n");
        let mut reader = BufReader::with_capacity(16 * 1024, stream);
        reader.get_mut().write_all(request.as_bytes()).await?;
        let mut bytes = Vec::new();
        loop {
            if bytes.len() >= MAX_HEADERS { return Err("oversized WebSocket handshake".into()); }
            bytes.push(reader.read_u8().await?);
            if bytes.ends_with(b"\r\n\r\n") { break; }
        }
        let text = std::str::from_utf8(&bytes)?;
        let mut lines = text.split("\r\n");
        let status = lines.next().ok_or("missing upgrade status")?;
        let parts: Vec<_> = status.splitn(3, ' ').collect();
        if parts.len() < 2 || parts[0] != "HTTP/1.1" || parts[1] != "101" { return Err("WebSocket upgrade status must be 101".into()); }
        let mut headers = BTreeMap::new();
        for line in lines.filter(|line| !line.is_empty()) {
            let (name, value) = line.split_once(':').ok_or("malformed upgrade header")?;
            let name = name.to_ascii_lowercase();
            if name.is_empty() || !name.bytes().all(|byte| byte.is_ascii_alphanumeric() || byte == b'-') { return Err("invalid upgrade header name".into()); }
            if headers.contains_key(&name) && matches!(name.as_str(), "upgrade" | "connection" | "sec-websocket-accept" | "sec-websocket-protocol" | "sec-websocket-extensions") { return Err("duplicate upgrade policy header".into()); }
            // Repeated unrelated fields (notably Set-Cookie) are legal and do not own negotiation.
            headers.insert(name, value.trim().to_string());
        }
        let header = |name: &str| headers.get(name).map(String::as_str).unwrap_or("");
        if !header("upgrade").eq_ignore_ascii_case("websocket") || !header("connection").split(',').any(|token| token.trim().eq_ignore_ascii_case("upgrade")) { return Err("invalid upgrade policy".into()); }
        let expected = tokio_tungstenite::tungstenite::handshake::derive_accept_key(key.as_bytes());
        if header("sec-websocket-accept") != expected || header("sec-websocket-protocol") != "actioncable-v1-json" { return Err("invalid WebSocket accept/subprotocol".into()); }
        let parameters = negotiate(headers.get("sec-websocket-extensions").map(String::as_str), requested)?;
        let compressed = parameters.is_some();
        if let Some(parameters) = parameters {
            counters.deflate_connections.fetch_add(1, Ordering::Relaxed);
            *counters.parameters.lock().unwrap().entry(parameters).or_insert(0) += 1;
        } else { counters.plain_connections.fetch_add(1, Ordering::Relaxed); }
        Ok(Self { reader, compressed, counters })
    }

    pub async fn send_text(&mut self, text: &str) -> Res<()> { self.send_frame(1, text.as_bytes()).await }

    async fn send_frame(&mut self, opcode: u8, payload: &[u8]) -> Res<()> {
        if payload.len() > MAX_MESSAGE { return Err("outbound message too large".into()); }
        let mask = random_bytes::<4>()?;
        let mut frame = vec![0x80 | opcode];
        match payload.len() {
            length if length < 126 => frame.push(0x80 | length as u8),
            length if length <= u16::MAX as usize => { frame.push(0xfe); frame.extend_from_slice(&(length as u16).to_be_bytes()); }
            length => { frame.push(0xff); frame.extend_from_slice(&(length as u64).to_be_bytes()); }
        }
        frame.extend_from_slice(&mask);
        frame.extend(payload.iter().enumerate().map(|(index, byte)| byte ^ mask[index % 4]));
        self.reader.get_mut().write_all(&frame).await?;
        Ok(())
    }

    async fn read_counted(&mut self, bytes: &mut [u8]) -> Res<()> {
        let mut offset = 0;
        while offset < bytes.len() {
            let count = self.reader.read(&mut bytes[offset..]).await?;
            if count == 0 { return Err("WebSocket EOF before close".into()); }
            self.counters.bytes.fetch_add(count as u64, Ordering::Relaxed);
            offset += count;
        }
        Ok(())
    }

    async fn frame(&mut self) -> Res<Frame> {
        let mut head = [0u8; 2];
        self.read_counted(&mut head).await?;
        let final_frame = head[0] & 0x80 != 0;
        let compressed = head[0] & 0x40 != 0;
        let opcode = head[0] & 15;
        if head[0] & 0x30 != 0 || head[1] & 0x80 != 0 { return Err("reserved bits or masked server frame".into()); }
        if !matches!(opcode, 0 | 1 | 8 | 9 | 10) { return Err("unexpected WebSocket opcode".into()); }
        let length = match head[1] & 127 {
            126 => { let mut bytes = [0; 2]; self.read_counted(&mut bytes).await?; let length = u16::from_be_bytes(bytes) as u64; if length < 126 { return Err("noncanonical frame length".into()); } length },
            127 => { let mut bytes = [0; 8]; self.read_counted(&mut bytes).await?; let length = u64::from_be_bytes(bytes); if length <= u16::MAX as u64 || length >> 63 != 0 { return Err("noncanonical frame length".into()); } length },
            length => length as u64,
        };
        if length > MAX_MESSAGE as u64 { return Err("frame exceeds message bound".into()); }
        if opcode >= 8 && (!final_frame || compressed || length > 125) { return Err("invalid control frame".into()); }
        if compressed && (!self.compressed || opcode != 1) { return Err("RSV1 without negotiation or on continuation".into()); }
        let mut payload = vec![0; length as usize];
        self.read_counted(&mut payload).await?;
        if opcode >= 8 { self.counters.control.fetch_add(1, Ordering::Relaxed); }
        // Data frame counters are assigned in next_text, where continuation ownership is known.
        Ok(Frame { final_frame, compressed, opcode, payload })
    }

    pub async fn next_text(&mut self) -> Res<Option<String>> {
        let Some((compressed, payload)) = self.next_payload().await? else { return Ok(None); };
        let decoded = if compressed { inflate(payload)? } else { payload };
        Ok(Some(String::from_utf8(decoded)?))
    }

    pub async fn next_validated(&mut self, memo: &crate::frame_memo::Memo) -> Res<Option<(Arc<crate::frame_memo::Facts>, std::time::Instant)>> {
        let Some((compressed, payload)) = self.next_payload().await? else { return Ok(None); };
        let received = std::time::Instant::now();
        Ok(Some((memo.decode(compressed, payload).await?, received)))
    }

    async fn next_payload(&mut self) -> Res<Option<(bool, Vec<u8>)>> {
        let mut message = Vec::new();
        let mut fragmented = false;
        let mut compressed_message = false;
        loop {
            let frame = self.frame().await?;
            match frame.opcode {
                8 => { validate_close(&frame.payload)?; self.send_frame(8, &frame.payload).await?; return Ok(None); },
                9 => { self.send_frame(10, &frame.payload).await?; continue; },
                10 => continue,
                1 => { if fragmented { return Err("data frame interrupts fragmented message".into()); } compressed_message = frame.compressed; },
                0 => { if !fragmented { return Err("orphan continuation".into()); } },
                _ => return Err("invalid data opcode".into()),
            }
            let counter = if compressed_message { &self.counters.compressed_data } else { &self.counters.plain_data };
            counter.fetch_add(1, Ordering::Relaxed);
            if message.len() + frame.payload.len() > MAX_MESSAGE { return Err("fragmented message exceeds bound".into()); }
            message.extend_from_slice(&frame.payload);
            fragmented = !frame.final_frame;
            if !fragmented {
                return Ok(Some((compressed_message, message)));
            }
        }
    }
}

struct Frame { final_frame: bool, compressed: bool, opcode: u8, payload: Vec<u8> }

fn negotiate(value: Option<&str>, requested: bool) -> Res<Option<String>> {
    let Some(value) = value else { return Ok(None); };
    if !requested || value.trim().is_empty() || value.contains(',') { return Err("unsolicited/empty/multiple WebSocket extensions".into()); }
    let mut pieces = value.split(';').map(str::trim);
    if pieces.next() != Some("permessage-deflate") { return Err("unknown WebSocket extension".into()); }
    let mut parameters = BTreeMap::new();
    for piece in pieces {
        let (name, value) = piece.split_once('=').map(|(name, value)| (name.trim(), Some(value.trim()))).unwrap_or((piece, None));
        if parameters.insert(name, value).is_some() { return Err("duplicate compression parameter".into()); }
        match (name, value) {
            ("server_no_context_takeover" | "client_no_context_takeover", None) => {},
            ("server_max_window_bits" | "client_max_window_bits", Some(value)) if matches!(value, "8" | "9" | "10" | "11" | "12" | "13" | "14" | "15") => {},
            _ => return Err("unsupported compression parameter".into()),
        }
    }
    if !parameters.contains_key("server_no_context_takeover") { return Err("server_no_context_takeover required by reset-per-message decoder".into()); }
    let normalized = parameters.iter().map(|(name, value)| match value { Some(value) => format!("{name}={value}"), None => name.to_string() }).collect::<Vec<_>>().join("; ");
    Ok(Some(format!("permessage-deflate; {normalized}")))
}

pub(crate) fn inflate(mut compressed: Vec<u8>) -> Res<Vec<u8>> {
    compressed.extend_from_slice(&[0, 0, 0xff, 0xff]);
    let mut inflater = flate2::Decompress::new(false);
    let mut result = Vec::new();
    let mut offset = 0;
    loop {
        let mut chunk = [0; 8192];
        let before_in = inflater.total_in();
        let before_out = inflater.total_out();
        let status = inflater.decompress(&compressed[offset..], &mut chunk, flate2::FlushDecompress::Sync)?;
        let consumed = (inflater.total_in() - before_in) as usize;
        let produced = (inflater.total_out() - before_out) as usize;
        offset += consumed;
        if result.len() + produced > MAX_MESSAGE { return Err("inflated message exceeds bound".into()); }
        result.extend_from_slice(&chunk[..produced]);
        if status == flate2::Status::StreamEnd { return Err("unexpected final deflate block".into()); }
        if offset == compressed.len() && produced < chunk.len() { return Ok(result); }
        if consumed == 0 && produced == 0 { return Err("stalled/incomplete deflate payload".into()); }
    }
}

fn validate_close(payload: &[u8]) -> Res<()> {
    if payload.len() == 1 { return Err("short close status".into()); }
    if payload.len() >= 2 {
        let status = u16::from_be_bytes([payload[0], payload[1]]);
        if !matches!(status, 1000..=1003 | 1007..=1014 | 3000..=4999) { return Err("invalid close status".into()); }
        std::str::from_utf8(&payload[2..])?;
    }
    Ok(())
}

fn random_bytes<const N: usize>() -> Res<[u8; N]> { let mut bytes = [0; N]; std::fs::File::open("/dev/urandom")?.read_exact(&mut bytes)?; Ok(bytes) }
fn base64(bytes: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut result = String::new();
    for chunk in bytes.chunks(3) {
        let first = chunk[0] as usize;
        let second = chunk.get(1).copied().unwrap_or(0) as usize;
        let third = chunk.get(2).copied().unwrap_or(0) as usize;
        result.push(ALPHABET[first >> 2] as char);
        result.push(ALPHABET[((first & 3) << 4) | (second >> 4)] as char);
        result.push(if chunk.len() > 1 { ALPHABET[((second & 15) << 2) | (third >> 6)] as char } else { '=' });
        result.push(if chunk.len() > 2 { ALPHABET[third & 63] as char } else { '=' });
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::io::DuplexStream;

    async fn server_upgrade(mut stream: DuplexStream, extension: Option<&str>, frames: Vec<u8>, expect_offer: bool, mutation: Option<(&str, &str)>) -> Res<Vec<u8>> {
        let mut request = Vec::new();
        while !request.ends_with(b"\r\n\r\n") { request.push(stream.read_u8().await?); }
        let text = String::from_utf8(request)?;
        assert_eq!(text.contains("Sec-WebSocket-Extensions:"), expect_offer);
        assert!(text.contains("User-Agent: wire-test-client\r\n"));
        let key = text.lines().find_map(|line| line.strip_prefix("Sec-WebSocket-Key: ")).ok_or("owner missing actual key")?;
        let accept = tokio_tungstenite::tungstenite::handshake::derive_accept_key(key.trim().as_bytes());
        let extension = extension.map(|value| format!("Sec-WebSocket-Extensions: {value}\r\n")).unwrap_or_default();
        let mut response = format!("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: actioncable-v1-json\r\n{extension}\r\n");
        if let Some((before, after)) = mutation {
            if before == "ACCEPT" { response = response.replace(&format!("Sec-WebSocket-Accept: {accept}"), after); }
            else { assert!(response.contains(before)); response = response.replace(before, after); }
        }
        stream.write_all(response.as_bytes()).await?;
        stream.write_all(&frames).await?;
        stream.shutdown().await?;
        // Keep real transport alive until the client side closes; no manufactured metric JSON.
        let mut client_output = Vec::new();
        stream.read_to_end(&mut client_output).await?;
        Ok(client_output)
    }
    async fn with_frames(offer: bool, extension: Option<&'static str>, frames: Vec<u8>) -> (Res<Option<String>>, Arc<Counters>) {
        let (result, counters, _) = with_exchange(offer, extension, frames, None).await;
        (result, counters)
    }
    async fn with_exchange(offer: bool, extension: Option<&'static str>, frames: Vec<u8>, sent_text: Option<&str>) -> (Res<Option<String>>, Arc<Counters>, Vec<u8>) {
        let (client, server) = tokio::io::duplex(65536);
        let task = tokio::spawn(server_upgrade(server, extension, frames, offer, None));
        let counters = Arc::new(Counters::default());
        let result = tokio::time::timeout(std::time::Duration::from_secs(3), async {
            let mut ws = WireWs::handshake(client, "127.0.0.1:1", "synthetic=test", offer, Some("wire-test-client"), counters.clone()).await?;
            if let Some(text) = sent_text { ws.send_text(text).await?; }
            ws.next_text().await
        }).await.expect("real duplex owner bounded");
        let outgoing = task.await.expect("owner server join").expect("owner server I/O");
        (result, counters, outgoing)
    }
    fn text_frame(bits: u8, bytes: &[u8]) -> Vec<u8> {
        let mut result = vec![bits];
        match bytes.len() {
            length if length < 126 => result.push(length as u8),
            length if length <= u16::MAX as usize => { result.push(126); result.extend_from_slice(&(length as u16).to_be_bytes()); },
            length => { result.push(127); result.extend_from_slice(&(length as u64).to_be_bytes()); },
        }
        result.extend_from_slice(bytes); result
    }
    fn compressed(bytes: &[u8]) -> Vec<u8> {
        use std::io::Write;
        let mut encoder = flate2::write::DeflateEncoder::new(Vec::new(), flate2::Compression::default());
        encoder.write_all(bytes).unwrap(); encoder.flush().unwrap();
        let result = encoder.get_ref(); assert!(result.ends_with(&[0, 0, 0xff, 0xff])); result[..result.len()-4].to_vec()
    }
    #[tokio::test] async fn actual_plain_and_declined_offer_have_exact_nonzero_wire_counts() {
        for offer in [false, true] {
            let frames = text_frame(0x81, b"hello");
            let (text, counters) = with_frames(offer, None, frames.clone()).await;
            assert_eq!(text.unwrap(), Some("hello".into()));
            assert_eq!(counters.snapshot().bytes, frames.len() as u64);
            assert_eq!(counters.snapshot().plain_data, 1);
            assert_eq!(counters.negotiation_json(offer)["negotiated_plain"], 1);
            assert_eq!(counters.negotiation_json(offer)["negotiated_deflate"], 0);
        }
    }
    #[tokio::test] async fn negotiated_deflate_decodes_real_payload_and_small_plain_frames() {
        for (bits, payload, compressed_count) in [(0xc1, compressed(b"hello hello hello"), 1), (0x81, b"small".to_vec(), 0)] {
            let frames = text_frame(bits, &payload);
            let (text, counters) = with_frames(true, Some("permessage-deflate; server_no_context_takeover; client_no_context_takeover"), frames.clone()).await;
            assert_eq!(text.unwrap(), Some(if compressed_count == 1 { "hello hello hello" } else { "small" }.into()));
            assert_eq!(counters.snapshot().bytes, frames.len() as u64);
            assert_eq!(counters.snapshot().compressed_data, compressed_count);
            assert_eq!(counters.negotiation_json(true)["negotiated_deflate"], 1);
        }
    }
    #[tokio::test] async fn fragments_keep_message_ownership_and_ping_has_exact_wire_bytes() {
        let mut frames = text_frame(0x01, b"hel"); frames.extend(text_frame(0x89, b"p")); frames.extend(text_frame(0x80, b"lo"));
        let (text, counters) = with_frames(false, None, frames.clone()).await;
        assert_eq!(text.unwrap(), Some("hello".into())); assert_eq!(counters.snapshot().bytes, frames.len() as u64);
        assert_eq!(counters.snapshot().plain_data, 2); assert_eq!(counters.snapshot().control, 1);
    }
    #[tokio::test] async fn nearby_invalid_server_frames_fail_at_the_same_reader() {
        for frames in [text_frame(0xc1, b"x"), text_frame(0x80, b"x"), text_frame(0x09, b"x"), vec![0x81, 0x80], vec![0x81, 126, 0, 1], text_frame(0x81, &[0xff]), text_frame(0x88, &[0]), vec![0x81, 5, b'a'], vec![0x81, 127, 0x80, 0, 0, 0, 0, 0, 0, 0]] {
            assert!(with_frames(false, None, frames).await.0.is_err());
        }
    }
    #[tokio::test] async fn extended_lengths_count_actual_header_and_payload_bytes() {
        for length in [125, 126, 65535, 65536] {
            let body = vec![b'x'; length]; let frames = text_frame(0x81, &body);
            let expected_header = if length < 126 { 2 } else if length <= 65535 { 4 } else { 10 };
            assert_eq!(frames.len(), length + expected_header);
            let (result, counters) = with_frames(false, None, frames).await;
            assert_eq!(result.unwrap().unwrap().as_bytes(), body.as_slice());
            assert_eq!(counters.snapshot().bytes, (length + expected_header) as u64); assert_eq!(counters.snapshot().plain_data, 1);
        }
    }
    #[tokio::test] async fn unsolicited_or_wrong_negotiation_fails_before_marking_connected() {
        for (offer, extension) in [(false, "permessage-deflate; server_no_context_takeover"), (true, "permessage-deflate"), (true, "permessage-deflate; server_no_context_takeover=1"), (true, "permessage-deflate; server_no_context_takeover; server_no_context_takeover"), (true, "other; server_no_context_takeover"), (true, "permessage-deflate; server_no_context_takeover; server_max_window_bits=7")] {
            let (result, counters) = with_frames(offer, Some(extension), Vec::new()).await;
            assert!(result.is_err()); assert_eq!(counters.negotiation_json(offer)["negotiated_deflate"], 0);
        }
    }
    #[tokio::test] async fn wrong_upgrade_status_accept_or_protocol_is_rejected_by_actual_handshake() {
        for mutation in [("HTTP/1.1 101", "HTTP/1.1 200"), ("ACCEPT", "Sec-WebSocket-Accept: wrong"), ("Sec-WebSocket-Protocol: actioncable-v1-json", "Sec-WebSocket-Protocol: wrong"), ("Upgrade: websocket", "Upgrade: wrong")] {
            let (client, server) = tokio::io::duplex(65536);
            let task = tokio::spawn(server_upgrade(server, None, Vec::new(), false, Some(mutation)));
            let counters = Arc::new(Counters::default());
            let result = tokio::time::timeout(std::time::Duration::from_secs(3), WireWs::handshake(client, "127.0.0.1:1", "synthetic=test", false, Some("wire-test-client"), counters.clone())).await.expect("handshake timeout");
            assert!(result.is_err()); assert_eq!(counters.negotiation_json(false)["negotiated_plain"], 0);
            task.await.expect("owner join").expect("owner I/O");
        }
    }
    #[tokio::test] async fn reset_per_message_decodes_two_actual_independent_deflate_messages() {
        let messages = ["first first first", "second second second"];
        let mut frames = Vec::new(); for text in messages { frames.extend(text_frame(0xc1, &compressed(text.as_bytes()))); }
        let wire_length = frames.len();
        let (client, server) = tokio::io::duplex(65536);
        let task = tokio::spawn(server_upgrade(server, Some("permessage-deflate; server_no_context_takeover"), frames, true, None));
        let counters = Arc::new(Counters::default());
        tokio::time::timeout(std::time::Duration::from_secs(3), async {
            let mut ws = WireWs::handshake(client, "127.0.0.1:1", "synthetic=test", true, Some("wire-test-client"), counters.clone()).await.unwrap();
            for expected in messages { assert_eq!(ws.next_text().await.unwrap(), Some(expected.into())); }
        }).await.expect("handshake timeout");
        task.await.expect("owner join").expect("owner I/O");
        assert_eq!(counters.snapshot().bytes, wire_length as u64); assert_eq!(counters.snapshot().compressed_data, 2);
    }
    fn unmask_short_frame(bytes: &[u8], opcode: u8) -> Vec<u8> {
        assert!(bytes.len() >= 6); assert_eq!(bytes[0], 0x80 | opcode); assert_eq!(bytes[1] & 0x80, 0x80);
        let length = (bytes[1] & 0x7f) as usize; assert!(length < 126); assert_eq!(bytes.len(), 6 + length);
        bytes[6..].iter().enumerate().map(|(index, byte)| byte ^ bytes[2 + index % 4]).collect()
    }
    #[tokio::test] async fn actual_outgoing_text_pong_and_close_are_masked_and_keep_payload() {
        let (_, _, text) = with_exchange(false, None, text_frame(0x81, b"hello"), Some("subscribe")).await;
        assert_eq!(unmask_short_frame(&text, 1), b"subscribe");
        let mut frames = text_frame(0x89, b"ping"); frames.extend(text_frame(0x81, b"hello"));
        let (result, _, pong) = with_exchange(false, None, frames, None).await;
        assert_eq!(result.unwrap(), Some("hello".into())); assert_eq!(unmask_short_frame(&pong, 10), b"ping");
        let (result, counters, close) = with_exchange(false, None, text_frame(0x88, &[3, 232]), None).await;
        assert_eq!(result.unwrap(), None); assert_eq!(unmask_short_frame(&close, 8), [3, 232]); assert_eq!(counters.snapshot().control, 1);
    }
    #[tokio::test] async fn actual_fragmented_deflate_keeps_context_across_control_frame() {
        let body = compressed(b"fragmented compressed message"); let middle = body.len()/2;
        let mut frames = text_frame(0x41, &body[..middle]); frames.extend(text_frame(0x89, b"ping")); frames.extend(text_frame(0x80, &body[middle..]));
        let (result, counters, pong) = with_exchange(true, Some("permessage-deflate; server_no_context_takeover"), frames.clone(), None).await;
        assert_eq!(result.unwrap(), Some("fragmented compressed message".into())); assert_eq!(counters.snapshot().bytes, frames.len() as u64);
        assert_eq!(counters.snapshot().compressed_data, 2); assert_eq!(counters.snapshot().control, 1); assert_eq!(unmask_short_frame(&pong, 10), b"ping");
    }
    #[test] fn key_encoding_matches_rfc6455_accept_input_example() { assert_eq!(base64(b"the sample nonce"), "dGhlIHNhbXBsZSBub25jZQ=="); }
}
