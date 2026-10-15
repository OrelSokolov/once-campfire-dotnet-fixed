//! Bounded, exact-wire validation memo. A marked broadcast is checked against an
//! independent plain control subscription before its compressed bytes are reused.
use std::collections::{HashMap, HashSet};
use std::hash::{Hash, Hasher};
use std::sync::{Arc, Mutex};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;
use tokio::sync::{Notify, OnceCell, Semaphore};
use serde_json::{Value, json};
use crate::Res;

const SHARDS: usize = 16;
const BUDGET: usize = 64 * 1024 * 1024;
const GOLDEN_BUDGET: usize = 128 * 1024 * 1024;
type Key = (bool, Vec<u8>);
type Cell = Arc<OnceCell<Arc<Facts>>>;
#[derive(Default)]
struct Shard { entries: HashMap<Key, Cell>, charge: usize }
struct GoldenEntry { pending: Vec<u64>, charge: usize }
#[derive(Default)]
struct Golden { texts: HashMap<Arc<str>, GoldenEntry>, by_sequence: HashMap<u64, Vec<Arc<str>>>, charge: usize }

pub struct Facts { pub text: Arc<str>, pub markers: Vec<u64>, pub confirmation: bool, pub on_stream: bool }
pub struct Memo {
    shards: [Mutex<Shard>; SHARDS], golden: Mutex<Golden>, ready: Notify,
    initializers: Semaphore, streams: Vec<String>, subscriptions: HashSet<String>,
    hits: AtomicU64, validations: AtomicU64, golden_checks: AtomicU64, evictions: AtomicU64,
}
impl Memo {
    pub fn new(streams: Vec<String>, subscriptions: Vec<String>) -> Self {
        Self { shards: std::array::from_fn(|_| Mutex::new(Shard::default())), golden: Mutex::new(Golden::default()), ready: Notify::new(),
            initializers: Semaphore::new(16), streams, subscriptions: subscriptions.into_iter().collect(),
            hits: AtomicU64::new(0), validations: AtomicU64::new(0), golden_checks: AtomicU64::new(0), evictions: AtomicU64::new(0) }
    }
    fn facts(&self, text: String) -> Res<Arc<Facts>> {
        let value: Value = serde_json::from_str(&text)?;
        let object = value.as_object().ok_or("Cable text must be a JSON object")?;
        let confirmation = value.get("type").and_then(Value::as_str) == Some("confirm_subscription");
        if confirmation && !value.get("identifier").and_then(Value::as_str).is_some_and(|id| self.subscriptions.contains(id)) {
            return Err("confirmation on an unregistered identifier".into());
        }
        let mut markers = crate::markers(&text); markers.sort_unstable(); markers.dedup();
        let on_stream = if markers.is_empty() { true } else {
            let identifier: Value = serde_json::from_str(value.get("identifier").and_then(Value::as_str).ok_or("marked frame has no identifier")?)?;
            let stream = identifier.get("signed_stream_name").and_then(Value::as_str).ok_or("marked frame has no stream")?;
            self.streams.iter().any(|allowed| allowed == stream)
        };
        if !markers.is_empty() && (!on_stream || !object.get("message").is_some_and(Value::is_string)) {
            return Err("marked frame has the wrong stream or body shape".into());
        }
        Ok(Arc::new(Facts { text: text.into(), markers, confirmation, on_stream }))
    }
    pub fn add_golden(&self, text: String) -> Res<()> {
        let facts = self.facts(text)?;
        if facts.markers.is_empty() { return Ok(()); }
        let mut golden = self.golden.lock().unwrap();
        if golden.texts.contains_key(&facts.text) { return Ok(()); }
        let charge=facts.text.len()+256+facts.markers.len()*128;
        if golden.charge + charge > GOLDEN_BUDGET { return Err("plain golden census exceeded its registered 128 MiB budget".into()); }
        golden.charge += charge;
        for &seq in &facts.markers {golden.by_sequence.entry(seq).or_default().push(facts.text.clone());}
        golden.texts.insert(facts.text.clone(),GoldenEntry{pending:facts.markers.clone(),charge});
        drop(golden); self.ready.notify_waiters(); Ok(())
    }
    async fn check_golden(&self, facts: &Facts) -> Res<()> {
        if facts.markers.is_empty() { return Ok(()); }
        tokio::time::timeout(Duration::from_secs(30), async {
            loop {
                let notified = self.ready.notified(); tokio::pin!(notified); notified.as_mut().enable();
                if self.golden.lock().unwrap().texts.contains_key(facts.text.as_ref()) { return; }
                notified.await;
            }
        }).await.map_err(|_| "decoded frame differs from the independent plain golden subscription")?;
        self.golden_checks.fetch_add(1, Ordering::Relaxed); Ok(())
    }
    pub fn retire(&self, sequence:u64) {
        // Retire only after every workload client has validated and acknowledged
        // this delivery. Slow readers keep their golden; no time-based eviction.
        let mut golden=self.golden.lock().unwrap();
        for text in golden.by_sequence.remove(&sequence).unwrap_or_default() {
            let complete=if let Some(entry)=golden.texts.get_mut(&text){entry.pending.retain(|&seq|seq!=sequence);entry.pending.is_empty()}else{false};
            if complete {let entry=golden.texts.remove(&text).unwrap();golden.charge-=entry.charge;}
        }
    }
    pub async fn decode(&self, compressed: bool, payload: Vec<u8>) -> Res<Arc<Facts>> {
        if payload.len()+256>BUDGET/SHARDS {
            let _permit=self.initializers.acquire().await?;
            let bytes=if compressed {crate::wire_ws::inflate(payload)?}else{payload};
            let facts=self.facts(String::from_utf8(bytes)?)?;self.check_golden(&facts).await?;
            self.validations.fetch_add(1,Ordering::Relaxed);return Ok(facts);
        }
        let key = (compressed, payload);
        let mut hash = std::collections::hash_map::DefaultHasher::new(); key.hash(&mut hash);
        let shard = &self.shards[hash.finish() as usize % SHARDS];
        let cell = {
            let mut state = shard.lock().unwrap();
            if let Some(cell) = state.entries.get(&key) { self.hits.fetch_add(1, Ordering::Relaxed); cell.clone() }
            else {
                let charge = key.1.len() + 256;
                if state.charge + charge > BUDGET / SHARDS { self.evictions.fetch_add(state.entries.len() as u64, Ordering::Relaxed); state.entries.clear(); state.charge = 0; }
                let cell = Arc::new(OnceCell::new()); state.charge += charge; state.entries.insert(key.clone(), cell.clone()); cell
            }
        };
        let value = cell.get_or_try_init(|| async {
            let _permit = self.initializers.acquire().await?;
            let bytes = if compressed { crate::wire_ws::inflate(key.1.clone())? } else { key.1.clone() };
            let facts = self.facts(String::from_utf8(bytes)?)?;
            self.check_golden(&facts).await?;
            self.validations.fetch_add(1, Ordering::Relaxed);
            let mut state = shard.lock().unwrap();
            if state.entries.get(&key).is_some_and(|current| Arc::ptr_eq(current, &cell)) {
                state.charge += facts.text.len() + facts.markers.capacity() * 8 + 64;
                if state.charge > BUDGET / SHARDS { self.evictions.fetch_add(state.entries.len() as u64, Ordering::Relaxed); state.entries.clear(); state.charge = 0; }
            }
            Ok::<_, Box<dyn std::error::Error + Send + Sync>>(facts)
        }).await?;
        Ok(value.clone())
    }
    pub fn report(&self) -> Value {
        json!({"mode":"exact wire bytes; distinct marked text compared with an independent plain control subscription",
            "hits":self.hits.load(Ordering::Relaxed),"full_validations":self.validations.load(Ordering::Relaxed),
            "golden_checks":self.golden_checks.load(Ordering::Relaxed),"evictions":self.evictions.load(Ordering::Relaxed),
            "charge":self.shards.iter().map(|s|s.lock().unwrap().charge).sum::<usize>(),"budget":BUDGET,
            "golden_charge":self.golden.lock().unwrap().charge,"golden_budget":GOLDEN_BUDGET,
            "control_connections":1,"in_flight_initializers_bound":16,"decoded_message_bound":16*1024*1024})
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn text(body: &str, stream: &str) -> String { json!({"identifier":json!({"channel":"Turbo::StreamsChannel","signed_stream_name":stream}).to_string(),"message":body}).to_string() }
    #[tokio::test]
    async fn exact_repeats_share_facts_after_independent_golden_validation() {
        let memo=Memo::new(vec!["good".into()],vec![]);let body=text("<turbo-stream> bmk1z 👍 </turbo-stream>","good");memo.add_golden(body.clone()).unwrap();
        let first=memo.decode(false,body.clone().into_bytes()).await.unwrap();let second=memo.decode(false,body.into_bytes()).await.unwrap();assert!(Arc::ptr_eq(&first,&second));assert_eq!(first.markers,vec![1]);assert_eq!(memo.report()["full_validations"],1);
    }
    #[tokio::test(start_paused=true)]
    async fn a_corrupted_valid_json_byte_cannot_reuse_or_pass_the_golden() {
        let memo=Memo::new(vec!["good".into()],vec![]);memo.add_golden(text("bmk1z first","good")).unwrap();
        assert!(memo.decode(false,text("bmk1z fIrst","good").into_bytes()).await.is_err());
    }
    #[tokio::test]
    async fn wrong_stream_is_rejected_before_reuse() {
        let memo=Memo::new(vec!["good".into()],vec![]);assert!(memo.decode(false,text("bmk1z","wrong").into_bytes()).await.is_err());
    }
    #[tokio::test]
    async fn compressed_wire_repeats_match_the_plain_golden_exactly() {
        let memo=Memo::new(vec!["good".into()],vec![]);let body=text("bmk1z <turbo-stream>👍</turbo-stream>","good");memo.add_golden(body.clone()).unwrap();
        let mut encoder=flate2::Compress::new(flate2::Compression::new(6),false);let mut encoded=vec![0;4096];encoder.compress(body.as_bytes(),&mut encoded,flate2::FlushCompress::Sync).unwrap();encoded.truncate(encoder.total_out() as usize);assert_eq!(&encoded[encoded.len()-4..],&[0,0,255,255]);encoded.truncate(encoded.len()-4);
        let first=memo.decode(true,encoded.clone()).await.unwrap();let second=memo.decode(true,encoded).await.unwrap();assert_eq!(first.text.as_ref(),body);assert!(Arc::ptr_eq(&first,&second));
    }
    #[tokio::test]
    async fn goldens_are_released_only_after_all_of_their_markers_retire() {
        let memo=Memo::new(vec!["good".into()],vec![]);let body=text("bmk1z bmk2z","good");memo.add_golden(body.clone()).unwrap();
        memo.retire(1);assert!(memo.report()["golden_charge"].as_u64().unwrap()>0);memo.decode(false,body.into_bytes()).await.unwrap();memo.retire(2);assert_eq!(memo.report()["golden_charge"],0);
    }
    #[tokio::test]
    async fn churn_keeps_the_memo_charge_bounded_and_revalidates_evicted_bytes() {
        let memo=Memo::new(vec![],vec![]);
        for n in 0..18000 {let body=json!({"type":"ping","message":format!("{n}{}","x".repeat(4096))}).to_string();memo.decode(false,body.into_bytes()).await.unwrap();}
        assert!(memo.report()["charge"].as_u64().unwrap()<=BUDGET as u64);assert!(memo.report()["evictions"].as_u64().unwrap()>0);
    }
}
