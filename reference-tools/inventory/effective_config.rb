# The reference app's effective runtime configuration in production mode: what Rails actually set
# after load_defaults 8.2, the initializers and the environment file, for the settings the .NET
# port reproduces. Values are read from the live objects, not from the config files.
#
#   parity/bin/reference runner --seed default reference-tools/inventory/effective_config.rb \
#     > effective-config.json
#
# Needs a database (--seed default, or any prepared storage) for the SQLite pragmas, and the
# parity environment (parity/.env.reference: DISABLE_SSL is set, so SSL shows as off here).
require "json"

Rails.application.eager_load!

GEM_DIR = Gem.dir

def short_path(path)
  path = path.to_s
  return path.delete_prefix("#{Rails.root}/") if path.start_with?("#{Rails.root}/")
  if (m = path.match(%r{/bundler/gems/(rails)-[0-9a-f]+/(.+)\z}))
    "rails/#{m[2]}"
  elsif (m = path.match(%r{/gems/([^/]+)/(.+)\z}))
    "#{m[1]}/#{m[2]}"
  else
    path
  end
end

# A JSON-safe rendering of a configuration value.
def value(object)
  case object
  when nil, true, false, Integer, String then object
  when Float then object.finite? ? object : object.to_s
  when Symbol then object.to_s
  when ActiveSupport::Duration then { "duration" => object.inspect, "seconds" => object.to_i }
  when Proc, Method then "#{object.lambda? ? 'lambda' : 'proc'}@#{object.source_location&.then { |f, l| "#{short_path(f)}:#{l}" }}" rescue object.inspect
  when Module then object.name || object.inspect
  when Regexp then { "regexp" => object.source }
  when Pathname then object.to_s
  when Array, Set then object.map { |o| value(o) }
  when Hash then object.to_h { |k, v| [ k.to_s, value(v) ] }
  when ActiveSupport::OrderedOptions then object.to_h.to_h { |k, v| [ k.to_s, value(v) ] }
  else
    object.respond_to?(:to_h) && !object.is_a?(Struct) ? value(object.to_h) : object.inspect
  end
rescue => error
  "#{object.class}: #{error.class}"
end

def try_value
  value(yield)
rescue NameError, NoMethodError => error
  "unavailable: #{error.class}: #{error.message.lines.first.strip}"
end

config = Rails.application.config
connection = ActiveRecord::Base.connection
raw = connection.raw_connection

pragmas = %w[
  foreign_keys journal_mode synchronous mmap_size journal_size_limit cache_size busy_timeout
  temp_store page_size auto_vacuum wal_autocheckpoint locking_mode recursive_triggers secure_delete
  encoding case_sensitive_like cell_size_check defer_foreign_keys automatic_index
].to_h { |name| [ name, connection.select_value("PRAGMA #{name}") ] }

cable = ActionCable.server.config
cable_adapter_config = cable.cable.to_h.transform_values(&:to_s)
cable_adapter_config["url"] = cable_adapter_config["url"].to_s.sub(%r{//[^@/]*@}, "//REDACTED@") if cable_adapter_config["url"]

cache = Rails.cache
cache_options = cache.options.to_h.transform_values { |v| value(v) }

key_generator = Rails.application.key_generator

report = {
  "generator" => "reference-tools/inventory/effective_config.rb",
  "rails" => Rails.version,
  "rails_env" => Rails.env,
  "load_defaults" => config.loaded_config_version,
  "time_zone" => config.time_zone,
  "active_record" => {
    "default_timezone" => ActiveRecord.default_timezone,
    "database_config" => ActiveRecord::Base.connection_db_config.configuration_hash.transform_values(&:to_s),
    "adapter_class" => connection.class.name,
    "sqlite_version" => connection.select_value("select sqlite_version()"),
    "default_pragmas" => try_value { ActiveRecord::ConnectionAdapters::SQLite3Adapter::DEFAULT_PRAGMAS },
    "pragmas_on_a_connection" => pragmas,
    "busy_handler_note" => "timeout: 5000 in database.yml is applied with the sqlite3 gem's busy_handler_timeout (a Ruby-level busy handler that releases the GVL), so PRAGMA busy_timeout reads 0",
    "transaction_mode" => ActiveRecord::Base.connection_db_config.configuration_hash[:default_transaction_mode],
    "pool_size" => ActiveRecord::Base.connection_pool.size,
    "strict_strings_by_default" => try_value { ActiveRecord::ConnectionAdapters::SQLite3Adapter.strict_strings_by_default },
    "run_after_transaction_callbacks_in_order_defined" => ActiveRecord.run_after_transaction_callbacks_in_order_defined,
    "attributes_for_inspect" => try_value { ActiveRecord::Base.attributes_for_inspect },
    "partial_inserts" => ActiveRecord::Base.partial_inserts,
    "belongs_to_required_by_default" => ActiveRecord::Base.belongs_to_required_by_default,
    "raise_on_assign_to_attr_readonly" => try_value { ActiveRecord.raise_on_assign_to_attr_readonly },
    "encryption_hash_digest_class" => try_value { ActiveRecord::Encryption.config.hash_digest_class },
    "sqlite3_gem" => { "version" => SQLite3::VERSION, "sqlite_version" => SQLite3::SQLITE_VERSION }
  },
  "cache_store" => {
    "config" => value(config.cache_store),
    "class" => cache.class.name,
    "options" => cache_options,
    "redis_url_env" => ENV["REDIS_URL"].present?,
    "fragment_cache_perform_caching" => ActionController::Base.perform_caching
  },
  "session_store" => {
    "store" => value(config.session_store),
    "options" => value(config.session_options),
    "cookies_serializer" => value(config.action_dispatch.cookies_serializer),
    "use_authenticated_cookie_encryption" => value(config.action_dispatch.use_authenticated_cookie_encryption),
    "encrypted_cookie_cipher" => value(config.action_dispatch.encrypted_cookie_cipher),
    "authenticated_encrypted_cookie_salt" => value(config.action_dispatch.authenticated_encrypted_cookie_salt),
    "signed_cookie_salt" => value(config.action_dispatch.signed_cookie_salt),
    "signed_cookie_digest" => value(config.action_dispatch.signed_cookie_digest),
    "cookies_same_site_protection" => value(config.action_dispatch.cookies_same_site_protection),
    "use_cookies_with_metadata" => value(config.action_dispatch.use_cookies_with_metadata),
    "cookies_rotations" => config.action_dispatch.cookies_rotations.then { |r| { "signed" => r.signed.size, "encrypted" => r.encrypted.size } },
    "key_generator" => key_generator.class.name,
    "key_generator_hash_digest_class" => value(config.active_support.key_generator_hash_digest_class),
    "message_serializer" => value(config.active_support.message_serializer),
    "use_message_serializer_for_metadata" => value(config.active_support.use_message_serializer_for_metadata),
    "hash_digest_class" => value(config.active_support.hash_digest_class)
  },
  "csrf" => {
    "allow_forgery_protection" => ActionController::Base.allow_forgery_protection,
    "default_protect_from_forgery" => value(config.action_controller.default_protect_from_forgery),
    "application_controller_strategy" => try_value { ApplicationController.forgery_protection_strategy },
    "per_form_csrf_tokens" => ActionController::Base.per_form_csrf_tokens,
    "forgery_protection_origin_check" => ActionController::Base.forgery_protection_origin_check,
    "request_forgery_protection_token" => value(ActionController::Base.request_forgery_protection_token),
    "log_warning_on_csrf_failure" => ActionController::Base.log_warning_on_csrf_failure,
    "csrf_token_storage_strategy" => try_value { ApplicationController.csrf_token_storage_strategy.class },
    "action_controller_config" => value(config.action_controller.to_h.slice(
      :default_protect_from_forgery, :forgery_protection_origin_check, :per_form_csrf_tokens,
      :urlsafe_csrf_tokens, :allow_forgery_protection, :escape_json_responses, :raise_on_missing_callback_actions,
      :action_on_open_redirect, :action_on_path_relative_redirect, :raise_on_open_redirects, :wrap_parameters_by_default))
  },
  "ssl" => {
    "disable_ssl_env" => ENV["DISABLE_SSL"].present?,
    "assume_ssl" => config.assume_ssl,
    "force_ssl" => config.force_ssl,
    "ssl_options" => value(config.ssl_options),
    "note" => "production.rb: assume_ssl and force_ssl are ENV[\"DISABLE_SSL\"].blank?; both are false here because parity/.env.reference sets DISABLE_SSL"
  },
  # What each request's cookie jar and middleware actually read (Rails.application.env_config),
  # with key material reduced to its class.
  "request_env_config" => Rails.application.env_config.select { |k, _| k.start_with?("action_dispatch.") }
    .reject { |k, _| k.include?("secret") || k.end_with?(".logger", ".backtrace_cleaner") }
    .sort.to_h { |k, v|
      [ k, case v
           when ActiveSupport::CachingKeyGenerator, ActiveSupport::KeyGenerator then v.class.name
           when ActiveSupport::Messages::RotationConfiguration then { "signed" => v.signed.size, "encrypted" => v.encrypted.size }
           else value(v)
           end ]
    },
  "hosts" => { "hosts" => value(config.hosts), "host_authorization" => value(config.host_authorization) },
  "default_headers" => value(config.action_dispatch.default_headers),
  "public_file_server" => { "enabled" => config.public_file_server.enabled, "headers" => value(config.public_file_server.headers) },
  "action_cable" => {
    "mount_path" => cable.mount_path,
    "url" => cable.url,
    "worker_pool_size" => cable.worker_pool_size,
    "worker_pool_note" => "parity/docker/parity_action_cable.rb pins 1 in the parity image (stock Action Cable: 4)",
    "allowed_request_origins" => value(cable.allowed_request_origins),
    "allow_same_origin_as_host" => cable.allow_same_origin_as_host,
    "disable_request_forgery_protection" => cable.disable_request_forgery_protection,
    "adapter" => cable_adapter_config,
    "connection_class" => value(cable.connection_class.call),
    "log_tags" => value(cable.log_tags),
    "filter_parameters" => value(cable.filter_parameters),
    "precompile_assets" => try_value { cable.precompile_assets },
    "health_check_path" => try_value { cable.health_check_path },
    "protocols" => ActionCable::INTERNAL[:protocols],
    "beat_interval_seconds" => try_value { ActionCable::Server::Connections::BEAT_INTERVAL },
    "channel_classes" => ApplicationCable::Channel.descendants.map(&:name).sort
  },
  "active_job" => {
    "config_queue_adapter" => value(config.active_job.queue_adapter),
    "adapter_class" => ActiveJob::Base.queue_adapter.class.name,
    "enqueue_after_transaction_commit" => try_value { ActiveJob::Base.enqueue_after_transaction_commit },
    "resque_pool_yml" => (YAML.load_file(Rails.root.join("config/resque-pool.yml"), aliases: true) rescue nil),
    "job_classes" => ActiveJob::Base.descendants.map(&:name).compact.sort
  },
  "json" => {
    "escape_html_entities_in_json" => ActiveSupport.escape_html_entities_in_json,
    "escape_js_separators_in_json" => try_value { ActiveSupport.escape_js_separators_in_json },
    "escape_json_responses" => try_value { ActionController::Base.escape_json_responses },
    "use_standard_json_time_format" => ActiveSupport::JSON::Encoding.use_standard_json_time_format,
    "time_precision" => ActiveSupport::JSON::Encoding.time_precision,
    "json_encoder" => value(ActiveSupport::JSON::Encoding.json_encoder),
    "json_gem" => JSON::VERSION,
    "samples" => {
      "html_and_separators" => ActiveSupport::JSON.encode("<a href=\"x\">&amp;</a>    é 😀"),
      "time" => ActiveSupport::JSON.encode(Time.utc(2026, 3, 2, 16, 0, 0, 123456)),
      "float" => ActiveSupport::JSON.encode([ 1.0, 0.1, 1e20, 1.5e-7 ]),
      "big_decimal" => ActiveSupport::JSON.encode(BigDecimal("1.50"))
    }
  },
  "active_storage" => {
    "service" => value(config.active_storage.service),
    "service_class" => ActiveStorage::Blob.service.class.name,
    "service_root" => (ActiveStorage::Blob.service.root rescue nil),
    "variant_processor" => value(ActiveStorage.variant_processor),
    "analyzers" => value(ActiveStorage.analyzers),
    "previewers" => value(ActiveStorage.previewers),
    "track_variants" => ActiveStorage.track_variants,
    "resolve_model_to_route" => value(config.active_storage.resolve_model_to_route),
    "urls_expire_in" => value(ActiveStorage.urls_expire_in),
    "service_urls_expire_in" => value(ActiveStorage.service_urls_expire_in),
    "content_types_to_serve_as_binary" => value(ActiveStorage.content_types_to_serve_as_binary),
    "content_types_allowed_inline" => value(ActiveStorage.content_types_allowed_inline),
    "web_image_content_types" => value(ActiveStorage.web_image_content_types),
    "variable_content_types" => value(ActiveStorage.variable_content_types),
    "binary_content_type" => ActiveStorage.binary_content_type,
    "queues" => value(ActiveStorage.queues),
    "direct_uploads_size_limit" => try_value { config.active_storage.direct_uploads_size_limit },
    "draw_routes" => config.active_storage.draw_routes,
    "vips_block_untrusted" => try_value { Vips.respond_to?(:block_untrusted) ? "Vips.block_untrusted available" : "absent" }
  },
  "action_text" => {
    "sanitizer_vendor" => try_value { config.action_text.sanitizer_vendor },
    "action_view_sanitizer_vendor" => try_value { config.action_view.sanitizer_vendor },
    "allowed_tags" => try_value { ActionText::ContentHelper.allowed_tags&.to_a&.sort },
    "allowed_attributes" => try_value { ActionText::ContentHelper.allowed_attributes&.to_a&.sort }
  },
  "action_controller" => {
    "raise_on_missing_callback_actions" => try_value { ActionController::Base.raise_on_missing_callback_actions },
    "action_on_unpermitted_parameters" => try_value { ActionController::Parameters.action_on_unpermitted_parameters },
    "wrap_parameters" => try_value { ApplicationController._wrapper_options.to_h },
    "default_url_options" => value(Rails.application.routes.default_url_options),
    "perform_caching" => ActionController::Base.perform_caching
  },
  "action_view" => {
    "default_enforce_utf8" => try_value { ActionView::Helpers::FormTagHelper.default_enforce_utf8 },
    "button_to_generates_button_tag" => try_value { ActionView::Helpers::UrlHelper.button_to_generates_button_tag },
    "prepend_content_exfiltration_prevention" => try_value { ActionView::Helpers::ContentExfiltrationPreventionHelper.prepend_content_exfiltration_prevention },
    "annotate_rendered_view_with_filenames" => try_value { ActionView::Base.annotate_rendered_view_with_filenames }
  },
  "content_security_policy" => value(config.content_security_policy),
  "permissions_policy" => value(config.permissions_policy),
  "filter_parameters" => value(config.filter_parameters),
  "logging" => {
    "log_level" => value(config.log_level),
    "log_tags" => value(config.log_tags),
    "silence_healthcheck_path" => try_value { config.silence_healthcheck_path },
    "logger_formatter" => Rails.logger.formatter.class.name
  },
  "i18n" => { "default_locale" => I18n.default_locale, "available_locales" => I18n.available_locales, "fallbacks" => value(config.i18n.fallbacks) },
  "custom" => {
    "app_version" => config.app_version,
    "git_revision" => config.git_revision
  }
}

puts JSON.pretty_generate(report)
