# The workload env_trace.rb watches: eager load, then touch every subsystem that reads the
# environment lazily (database, cache, Action Cable, jobs, Web Push, storage), then send a few
# requests through the full middleware stack.
Rails.application.eager_load!
ActiveRecord::Base.connection.select_value("select 1")
Rails.cache.read("env-trace") rescue nil
ActionCable.server.config.cable
ActionCable.server.pubsub rescue nil
ActiveJob::Base.queue_adapter
ActiveStorage::Blob.service
WebPush.respond_to?(:vapid_identification) if defined?(WebPush)

session = ActionDispatch::Integration::Session.new(Rails.application)
session.host! "campfire.test"
%w[ /up /session/new / /webmanifest /service-worker ].each { |path| session.get(path) }
session.post "/session", params: { email_address: "nobody@example.com", password: "wrong" }
