# Exact JSON bytes for every JSON output of the reference: the eight Jbuilder views
# through real requests, UnfurlLinksController's `render json:`, the manifest, the stored JSON
# columns, the outbound webhook and Web Push payloads, Action Cable broadcasts and frames, and
# ActiveSupport::JSON.encode samples. See vectors/json/README.md.
#
#   parity/bin/reference runner --seed default reference-tools/json/generate.rb
#
# Runs on a throwaway copy of the default seed (production, parity/.env.reference), adds records
# with hostile strings at a frozen clock, sends requests in process through the whole Rails
# middleware stack, and writes vectors/json/<name>.json (the body bytes as Rails produced them)
# plus vectors/json/index.json (request, status, headers and inputs for each).
require "json"
require "active_support/testing/time_helpers"
require "action_dispatch/testing/integration"

OUT = Pathname.new(ENV.fetch("JSON_VECTORS_OUT", "/work/vectors/json"))
FIXTURES = Rails.root.join("test/fixtures/files")
NOW = Time.utc(2026, 3, 2, 16, 0, 0)
CHROME = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"
HEADERS = %w[ content-type cache-control etag location link x-total-count vary content-disposition ]

# Hostile text: HTML specials, both JavaScript line separators, non-ASCII, astral emoji,
# control characters, a backslash and a slash.
HOSTILE = %(<b>Zoë</b> & "Ünïcødé" 'quotes'  line para 😀 tab\tnewline\n bs\\ slash/ </script>)

include ActiveSupport::Testing::TimeHelpers
FileUtils.mkdir_p OUT
INDEX = []

def write_vector(name, bytes, **details)
  File.binwrite(OUT.join("#{name}.json"), bytes)
  INDEX << { name: name, file: "#{name}.json", bytes: bytes.bytesize, sha256: Digest::SHA256.hexdigest(bytes) }.merge(details)
end

def capture(session, name, verb, path, source:, params: nil, headers: {}, body: nil, **details)
  request_headers = { "User-Agent" => CHROME, "Accept" => "application/json" }.merge(headers)
  if body
    # A String as params is the raw request body (the bot API reads request.body).
    session.process(verb, path, params: body, headers: request_headers.merge("Content-Type" => "text/plain; charset=utf-8"))
  else
    session.process(verb, path, params: params, headers: request_headers)
  end
  response = session.response
  unless response.status.between?(200, 299)
    raise "#{name}: #{verb.upcase} #{path} answered #{response.status}: #{response.body[0, 300]}"
  end
  write_vector name, response.body.b, source: source,
    request: { method: verb.to_s.upcase, path: path, params: params, body: body, headers: headers.except("X-CSRF-Token") }.compact,
    status: response.status, headers: response.headers.to_h.transform_keys(&:downcase).slice(*HEADERS), **details
end

def sign_in(session, email)
  session.get "/session/new", headers: { "User-Agent" => CHROME }
  token = session.response.body[/name="authenticity_token" value="([^"]+)"/, 1] or raise "no token on /session/new"
  session.post "/session", params: { authenticity_token: token, email_address: email, password: "secret123456" }, headers: { "User-Agent" => CHROME }
  raise "sign-in failed: #{session.response.status}" unless session.response.redirect?
end

def csrf_token(session)
  session.get "/users/me/profile", headers: { "User-Agent" => CHROME }
  session.response.body[/<meta name="csrf-token" content="([^"]+)"/, 1] or raise "no csrf-token meta"
end

# Every string Action Cable publishes, as its pubsub adapter receives it.
module RecordBroadcasts
  RECORDED = []
  def broadcast(channel, payload)
    RECORDED << [ channel, payload ]
    super
  end
end

# The unfurl fetch never leaves the process: a public address, a fixed page, a PNG image.
UNFURL_HTML = <<~HTML
  <html><head><meta charset="utf-8">
  <meta property="og:title" content="#{ERB::Util.html_escape(HOSTILE)}">
  <meta property="og:description" content="Tags &lt;i&gt;stripped&lt;/i&gt; &amp; entities,   separators, naïve café 😀">
  <meta property="og:image" content="https://unfurl.example/og.png">
  <meta property="og:url" content="https://unfurl.example/canonical?a=1&amp;b=2">
  </head><body></body></html>
HTML
RestrictedHTTP::PrivateNetworkGuard.singleton_class.prepend(Module.new { def resolve(_host) = "93.184.216.34" })
Opengraph::Fetch.prepend(Module.new {
  def fetch_document(_url, ip: nil) = UNFURL_HTML
  def fetch_content_type(_url, ip: nil) = "image/png"
})

travel_to NOW

account = Account.sole
david = User.find_by!(email_address: "david@37signals.com")
kevin = User.find_by!(email_address: "kevin@37signals.com")
account.update!(name: %(Signal & <Noise> "HQ" ’   😀))
# accounts.settings is Rails 8.2's has_json (ActiveModel::SchematizedJson): only the declared
# boolean key exists. Record the seeded value, then the value after each assignment form.
settings_sql = "SELECT settings FROM accounts WHERE id = #{account.id}"
settings_writes = [ [ "seeded", ActiveRecord::Base.connection.select_value(settings_sql) ] ]
account.settings.restrict_room_creation_to_administrators = true
account.save!
settings_writes << [ "accessor_true", ActiveRecord::Base.connection.select_value(settings_sql) ]
account.update!(settings: { restrict_room_creation_to_administrators: "0" })
settings_writes << [ "hash_string_zero", ActiveRecord::Base.connection.select_value(settings_sql) ]
# What Account.create! writes when settings are never touched (first run), rolled back because
# accounts is a singleton table.
Account.transaction do
  created = Account.create!(name: "Fresh", singleton_guard: 1)
  settings_writes << [ "created_untouched", ActiveRecord::Base.connection.select_value("SELECT settings FROM accounts WHERE id = #{created.id}") ]
  raise ActiveRecord::Rollback
end
account.update!(settings: { restrict_room_creation_to_administrators: true })

zoe = User.create!(name: HOSTILE.delete("\t\n"), email_address: "zoe@example.com", password: "secret123456", bio: HOSTILE)
bot = User.create_bot!(name: %(Böt <"&">))
bot.update_columns(bot_token: "JsonVectorBot1") # generated at random; the key shows up in payloads
room = Rooms::Closed.create_for({ name: %(Edge <cases> & “quotes”), creator: david }, users: [ david, kevin, zoe, bot ])

# CGI.escapeHTML, not ERB::Util.html_escape: the rendered partial is html_safe, which html_escape
# would return unescaped and so break the attribute.
mention_content = CGI.escapeHTML(ApplicationController.render(partial: "users/mention", locals: { user: kevin }).to_str)
mention = %(<action-text-attachment sgid="#{kevin.attachable_sgid}" content-type="application/vnd.campfire.mention" content="#{mention_content}"></action-text-attachment>)
bodies = [
  "<p>Plain &amp; simple</p>",
  "<p>#{ERB::Util.html_escape(HOSTILE)}</p>",
  "<p>Hey #{mention}, <strong>bold</strong> <em>é</em> and a <a href=\"https://example.com/?a=1&amp;b=2\">link</a></p>"
]
# 38 older messages, so the room has more than a page (Message::Pagination::PAGE_SIZE = 40) and
# the bot index carries a Link header.
38.times do |i|
  travel_to NOW - 3.hours + i.minutes
  room.messages.create!(creator: [ david, kevin ][i % 2], body: "<p>Filler #{i + 1}</p>", client_message_id: "json-vector-filler-#{i + 1}")
end
messages = bodies.each_with_index.map do |body, i|
  travel_to NOW - 10.minutes + i.minutes
  room.messages.create!(creator: [ david, zoe, david ][i], body: body, client_message_id: "json-vector-#{i}")
end
travel_to NOW - 5.minutes
image_message = room.messages.create_with_attachment!(creator: zoe, client_message_id: "json-vector-image",
  attachment: { io: File.open(FIXTURES.join("moon.jpg")), filename: "moon ☾ <1>.jpg", content_type: "image/jpeg" })
image_message.attachment.analyze
travel_to NOW - 4.minutes
messages.first.boosts.create!(booster: zoe, content: %(🔥 <3 & "x"))
travel_to NOW

ActionCable.server.pubsub.class.prepend(RecordBroadcasts)
session = ActionDispatch::Integration::Session.new(Rails.application)
session.host! "campfire.test"
bot_base = "/rooms/#{room.id}/#{bot.bot_key}/messages"

# Jbuilder: messages/by_bots/index -> messages/_message -> users/_user
capture session, "by_bots_index", :get, bot_base, source: "messages/by_bots/index.json.jbuilder"
capture session, "by_bots_index_before", :get, bot_base, params: { before: messages.last.id }, source: "messages/by_bots/index.json.jbuilder (paged, Link header)"

# Jbuilder: messages/by_bots/show, through MessagesController#update's format.json { render :show }
bot_message = room.messages.create!(creator: bot, body: "<p>From the bot</p>", client_message_id: "json-vector-bot")
capture session, "by_bots_update", :put, "#{bot_base}/#{bot_message.id}", body: %(Edited by the bot: #{HOSTILE}), source: "messages/by_bots/show.json.jbuilder"

# Jbuilder: messages/boosts/by_bots/show -> messages/boosts/_boost -> users/_user
capture session, "boosts_by_bots_create", :post, "#{bot_base}/#{messages.first.id}/boosts", body: %(👍 & <ok> " "), source: "messages/boosts/by_bots/show.json.jbuilder"

# Jbuilder: autocompletable/users/index -> autocompletable/users/_user (signed in as David)
sign_in session, "david@37signals.com"
capture session, "autocompletable_users", :get, "/autocompletable/users.json", source: "autocompletable/users/index.json.jbuilder"
capture session, "autocompletable_users_query", :get, "/autocompletable/users.json", params: { query: "Zo" }, source: "autocompletable/users/index.json.jbuilder (filtered)"
capture session, "autocompletable_users_room", :get, "/autocompletable/users.json", params: { room_id: room.id }, source: "autocompletable/users/index.json.jbuilder (room members)"

# render json: in UnfurlLinksController (escape_json_responses = false)
token = csrf_token(session)
capture session, "unfurl_link", :post, "/unfurl_link", params: { url: "https://unfurl.example/page" }, headers: { "X-CSRF-Token" => token }, source: "UnfurlLinksController#create render json: (fetch stubbed)"

# pwa/manifest.json.erb (ERB, so HTML-escaped inside JSON strings)
capture session, "webmanifest", :get, "/webmanifest", headers: { "Accept" => "*/*" }, source: "pwa/manifest.json.erb"

# Stored JSON columns, as written.
connection = ActiveRecord::Base.connection
write_vector "column_accounts_settings", JSON.pretty_generate(settings_writes.to_h).b,
  source: "accounts.settings raw column text (has_json): as seeded, after settings.restrict_room_creation_to_administrators = true, after update!(settings: { ...: \"0\" }), and as Account.create! writes it untouched"
blob_rows = connection.select_rows("SELECT id, filename, content_type, metadata FROM active_storage_blobs ORDER BY id")
blob_rows.group_by { |_, _, content_type, _| content_type }.each do |content_type, rows|
  id, filename, _, metadata = rows.first
  write_vector "column_blob_metadata_#{content_type.tr("/", "_")}", metadata.to_s.b,
    source: "active_storage_blobs.metadata (ActiveRecord::Coders::JSON) of blob #{id}", filename: filename
end
write_vector "column_blob_metadata_new_image", connection.select_value("SELECT metadata FROM active_storage_blobs WHERE id = #{image_message.attachment.blob.id}").b,
  source: "active_storage_blobs.metadata after create_with_attachment! and analyze (moon.jpg)"

# Outbound payloads.
webhook = Webhook.new(user: bot, url: "https://hooks.example/bot")
write_vector "webhook_payload", webhook.send(:payload, messages[2]).b, source: "Webhook#payload (Hash#to_json)", message_id: messages[2].id
subscription = david.push_subscriptions.first || david.push_subscriptions.create!(endpoint: "https://push.example/1", p256dh_key: "p", auth_key: "a", user_agent: CHROME)
payload = Room::MessagePusher.new(room: room, message: messages[1]).send(:build_payload)
write_vector "web_push_payload", subscription.notification(**payload).send(:encoded_message).b,
  source: "WebPush::Notification#encoded_message (JSON.generate, not ActiveSupport)", message_id: messages[1].id

# Action Cable: what the channels and models publish, and the frame a subscriber receives.
RecordBroadcasts::RECORDED.clear
image_message.broadcast_create
TypingNotificationsChannel.broadcast_to room, action: :start, user: zoe.slice(:id, :name)
ActionCable.server.broadcast "user_#{david.id}_reads", { room_id: room.id }
recorded = RecordBroadcasts::RECORDED.dup
cable = recorded.map.with_index do |(channel, encoded), i|
  identifier = case channel
    when /\Atyping_notifications:/ then { channel: "TypingNotificationsChannel", room_id: room.id }.to_json
    when /\Auser_\d+_reads\z/ then { channel: "ReadRoomsChannel" }.to_json
    when /\Auser_\d+_unreads\z/ then { channel: "UnreadRoomsChannel" }.to_json
    else { channel: "Turbo::StreamsChannel", signed_stream_name: Turbo::StreamsChannel.signed_stream_name([ room, :messages ]) }.to_json
  end
  # ActionCable::Channel::Streams decodes the broadcast with ActiveSupport::JSON and transmits
  # { identifier:, message: }, which the connection encodes with ActiveSupport::JSON again.
  frame = ActiveSupport::JSON.encode({ "identifier" => identifier, "message" => ActiveSupport::JSON.decode(encoded) })
  { broadcasting: channel, pubsub_payload: encoded, subscriber_identifier: identifier, frame: frame }
end
write_vector "cable_broadcasts", JSON.pretty_generate(cable).b, source: "ActionCable.server.broadcast payloads (recorded at the pubsub adapter) and the subscriber frames they become"

# ActiveSupport::JSON.encode on the values the port's writer must handle.
samples = {
  "html_specials" => %(<a href="x">&amp;</a> 'single' </script>),
  "separators" => "line para end",
  "non_ascii" => "Zoë café naïve 日本語 ’",
  "astral" => "😀🔥",
  "controls" => (0..31).map(&:chr).join + "\u007f",
  "backslash_slash" => "a\\b/c",
  "integer_big" => 2**64,
  "floats" => [ 0.0, -0.0, 1.0, 0.1, 1.5, 100.0, 1e15, 1e16, 1e20, 1.0e-5, 1.5e-7, 123456789.123456789, 2.0**70 ],
  "float_special" => [ Float::NAN, Float::INFINITY, -Float::INFINITY ],
  "big_decimal" => [ BigDecimal("1.50"), BigDecimal("0.1"), BigDecimal("1e20") ],
  "times" => {
    "time_utc" => Time.utc(2026, 3, 2, 16, 0, 0),
    "time_usec" => Time.utc(2026, 3, 2, 16, 0, 0, 123456),
    "time_offset" => Time.new(2026, 3, 2, 17, 0, 0, "+01:00"),
    "time_with_zone" => Time.utc(2026, 3, 2, 16, 0, 0, 999999).in_time_zone("UTC"),
    "time_with_zone_berlin" => Time.utc(2026, 3, 2, 16, 0, 0).in_time_zone("Berlin"),
    "date" => Date.new(2026, 3, 2),
    "datetime" => DateTime.new(2026, 3, 2, 16, 0, 0),
    "duration" => 3.minutes
  },
  "symbols_and_nil" => { sym: :value, nil: nil, true: true, false: false, "string key" => [ :a, nil ] },
  "nested" => { a: [ { b: { c: [ 1, "two", 3.0 ] } } ] },
  "hostile" => HOSTILE
}
encoded = samples.transform_values { |value| ActiveSupport::JSON.encode(value) }
write_vector "activesupport_encode", JSON.pretty_generate(encoded).b,
  source: "ActiveSupport::JSON.encode(value) for each key; values are the Ruby inputs described in the README",
  flags: { escape_html_entities_in_json: ActiveSupport.escape_html_entities_in_json, escape_js_separators_in_json: ActiveSupport.escape_js_separators_in_json,
           time_precision: ActiveSupport::JSON::Encoding.time_precision, use_standard_json_time_format: ActiveSupport::JSON::Encoding.use_standard_json_time_format }
write_vector "to_json_vs_json_generate", JSON.pretty_generate(
  "to_json" => HOSTILE.to_json, "hash_to_json" => { text: HOSTILE }.to_json,
  "json_generate" => JSON.generate(HOSTILE), "hash_json_generate" => JSON.generate({ text: HOSTILE }),
  "json_dump" => JSON.dump(HOSTILE)).b,
  source: "Object#to_json (ActiveSupport) against JSON.generate/JSON.dump (the json gem), on HOSTILE"

File.write(OUT.join("index.json"), JSON.pretty_generate(
  generator: "reference-tools/json/generate.rb", rails: Rails.version, clock: NOW.iso8601, host: "campfire.test",
  hostile: HOSTILE, records: { account: account.id, room: room.id, zoe: zoe.id, bot: bot.id, messages: messages.map(&:id), image_message: image_message.id, bot_message: bot_message.id },
  vectors: INDEX) + "\n")
