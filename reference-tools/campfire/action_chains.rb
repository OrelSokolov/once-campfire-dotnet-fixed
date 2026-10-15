# The callback chain of every routed controller#action, as the reference would run it, for the
# .NET policy filter. Extends callbacks.rb, which prints raw chains
# of a few controllers with condition counts.
#
#   parity/bin/reference runner --seed default reference-tools/campfire/action_chains.rb \
#     > vectors/policy/action_chains.json
#
# For each route whose controller exists, and each action, the chain lists the process_action
# callbacks in chain order (before and around callbacks run in this order; after callbacks run in
# reverse order once the action returns). `only:`/`except:` conditions (ActionFilter) are resolved
# for that action: callbacks they exclude are left out, and the ones they keep carry no trace of
# them. Every other condition (a method or a proc) is kept and named by kind (if/unless) and
# source location, because it depends on the request. Procs from Rails' rate_limit and
# allow_browser also carry their arguments.
#
# Also recorded: every concrete controller with its action methods; routes whose controller does
# not exist (rooms/settings); and the responses the Action Mailbox and conductor routes, the
# missing controller and the routed actions with no method give to anonymous requests, sent in
# process through the whole Rails middleware stack (without config.ru's Rack::Deflater and
# Thruster).
require "json"
require "digest"

Rails.application.eager_load!

ACTION_FILTER = AbstractController::Callbacks::ActionFilter

def short_path(path)
  path = path.to_s
  return path.delete_prefix("#{Rails.root}/") if path.start_with?("#{Rails.root}/")
  if (m = path.match(%r{/bundler/gems/rails-[0-9a-f]+/(.+)\z}))
    "rails/#{m[1]}"
  elsif (m = path.match(%r{/gems/([^/]+)/(.+)\z}))
    "#{m[1]}/#{m[2]}"
  else
    path
  end
end

def source_line(path, line)
  return nil unless path && line && File.file?(path)
  File.readlines(path)[line - 1]&.strip
rescue
  nil
end

def location(path, line)
  path && "#{short_path(path)}:#{line}"
end

def method_info(controller, name)
  method = controller.instance_method(name)
  path, line = method.source_location
  { "owner" => method.owner.name || method.owner.inspect, "defined_at" => location(path, line) }
rescue NameError
  { "owner" => nil, "defined_at" => nil, "missing" => true }
end

# Arguments of Rails' own before_action procs, read from the proc's binding.
MACRO_ARGUMENTS = {
  "rate_limiting.rb" => [ "rate_limit", %i[to within by with store name scope] ],
  "allow_browser.rb" => [ "allow_browser", %i[versions block] ]
}

def plain(value)
  case value
  when nil, true, false, Integer, Float, String then value
  when Symbol then value.to_s
  when ActiveSupport::Duration then { "duration" => value.inspect, "seconds" => value.to_i }
  when Proc then { "proc" => location(*value.source_location), "code" => source_line(*value.source_location) }
  when Hash then value.to_h { |k, v| [ k.to_s, plain(v) ] }
  when Array then value.map { |v| plain(v) }
  when ActiveSupport::Cache::Store then value.class.name
  else value.inspect
  end
end

def describe_filter(controller, filter)
  case filter
  when Symbol, String
    { "type" => "method", "name" => filter.to_s }.merge(method_info(controller, filter))
  when Proc
    path, line = filter.source_location
    info = { "type" => "proc", "defined_at" => location(path, line), "code" => source_line(path, line) }
    macro, names = MACRO_ARGUMENTS.find { |file, _| path.to_s.end_with?("/#{file}") }&.last
    if macro
      binding = filter.binding
      info["macro"] = macro
      info["arguments"] = names.select { |n| binding.local_variable_defined?(n) }.to_h { |n| [ n.to_s, plain(binding.local_variable_get(n)) ] }
    end
    info
  else
    { "type" => "object", "class" => filter.class.name }
  end
end

def describe_condition(controller, kind, condition)
  case condition
  when Symbol, String
    { "kind" => kind, "type" => "method", "name" => condition.to_s }.merge(method_info(controller, condition))
  when Proc
    path, line = condition.source_location
    { "kind" => kind, "type" => "proc", "defined_at" => location(path, line), "code" => source_line(path, line) }
  else
    { "kind" => kind, "type" => "object", "class" => condition.class.name }
  end
end

def action_filter_actions(filter)
  filter.instance_variable_get(:@actions).to_a.sort
end

# The callbacks that can run for `action`, in chain order, with ActionFilter conditions resolved.
def chain_for(controller, action)
  controller._process_action_callbacks.filter_map do |callback|
    ifs = callback.instance_variable_get(:@if)
    unlesses = callback.instance_variable_get(:@unless)
    next unless ifs.grep(ACTION_FILTER).all? { |f| action_filter_actions(f).include?(action) }
    next if unlesses.grep(ACTION_FILTER).any? { |f| action_filter_actions(f).include?(action) }

    conditions = ifs.grep_v(ACTION_FILTER).map { |c| describe_condition(controller, "if", c) } +
      unlesses.grep_v(ACTION_FILTER).map { |c| describe_condition(controller, "unless", c) }
    { "kind" => callback.kind.to_s }.merge(describe_filter(controller, callback.filter)).merge("conditions" => conditions)
  end
end

# The whole chain with its only/except lists, for reference.
def raw_chain(controller)
  controller._process_action_callbacks.map do |callback|
    ifs = callback.instance_variable_get(:@if)
    unlesses = callback.instance_variable_get(:@unless)
    { "kind" => callback.kind.to_s }.merge(describe_filter(controller, callback.filter)).merge(
      "only" => ifs.grep(ACTION_FILTER).map { |f| action_filter_actions(f) },
      "except" => unlesses.grep(ACTION_FILTER).map { |f| action_filter_actions(f) },
      "conditions" => ifs.grep_v(ACTION_FILTER).map { |c| describe_condition(controller, "if", c) } +
        unlesses.grep_v(ACTION_FILTER).map { |c| describe_condition(controller, "unless", c) })
  end
end

def action_status(controller, action)
  if controller.action_methods.include?(action)
    "method"
  else
    instance = controller.new
    instance.available_action?(action) ? "implicit_render" : "missing"
  end
rescue => error
  "unknown (#{error.class})"
end

routes = Rails.application.routes.routes.reject { |r| r.verb.blank? || r.internal }.map do |route|
  { verb: route.verb, path: route.path.spec.to_s, controller: route.defaults[:controller], action: route.defaults[:action], defaults: route.defaults.except(:controller, :action) }
end

by_endpoint = routes.select { |r| r[:controller] }.group_by { |r| [ r[:controller], r[:action] ] }
missing_controllers = []
actions = {}
routed_controllers = {}

by_endpoint.sort.each do |(controller_path, action), endpoint_routes|
  class_name = "#{controller_path.camelize}Controller"
  controller = class_name.safe_constantize
  route_list = endpoint_routes.map { |r| "#{r[:verb]} #{r[:path]}" }
  unless controller
    missing_controllers << { "endpoint" => "#{controller_path}##{action}", "controller_class" => class_name, "routes" => route_list }
    next
  end

  (routed_controllers[controller] ||= []) << action
  actions["#{controller_path}##{action}"] = {
    "controller" => controller.name,
    "routes" => route_list,
    "defaults" => endpoint_routes.first[:defaults].transform_keys(&:to_s).transform_values(&:to_s),
    "action" => action_status(controller, action),
    "chain" => chain_for(controller, action)
  }
end

concrete = (ActionController::Base.descendants + ActionController::API.descendants)
  .reject { |k| k.name.nil? || k.abstract? }
  .sort_by(&:name)
controllers = concrete.to_h do |controller|
  path, line = Object.const_source_location(controller.name)
  [ controller.name, {
    "superclass" => controller.superclass.name,
    "defined_at" => location(path, line),
    "ancestors_modules" => controller.ancestors.take_while { |a| a != ActionController::Base && a != ActionController::API }
      .grep_v(Class).map { |m| m.name }.compact.select { |n| !n.start_with?("#<") },
    "action_methods" => controller.action_methods.to_a.sort,
    "routed_actions" => (routed_controllers[controller] || []).sort,
    "raw_chain" => raw_chain(controller)
  } ]
end

# Anonymous in-process requests to the framework routes whose behavior the port must copy and
# to the declared route without a controller.
def probe(session, verb, path)
  session.process(verb.downcase.to_sym, path, headers: { "Accept" => "text/html" })
  response = session.response
  body = response.body.to_s
  { "request" => "#{verb} #{path}", "status" => response.status,
    "content_type" => response.headers["content-type"], "location" => response.headers["location"],
    "www_authenticate" => response.headers["www-authenticate"],
    "body_bytes" => body.bytesize, "body_sha256" => Digest::SHA256.hexdigest(body),
    "body_start" => body[0, 120] }
rescue => error
  { "request" => "#{verb} #{path}", "raised" => "#{error.class}: #{error.message.lines.first&.strip}" }
end

session = ActionDispatch::Integration::Session.new(Rails.application)
session.host! "campfire.test"
# Routed actions without a method or template raise AbstractController::ActionNotFound before any
# callback runs, so they are probed too.
missing_actions = actions.select { |_, a| a["action"] == "missing" }.keys
probe_paths = routes.select { |r|
  endpoint = "#{r[:controller]}##{r[:action]}"
  r[:controller].to_s.start_with?("action_mailbox/", "rails/conductor/") ||
    missing_controllers.any? { |m| m["endpoint"] == endpoint } || missing_actions.include?(endpoint)
}.map { |r| [ r[:verb], r[:path].sub("(.:format)", "").gsub(/:[a-z_]+/, "1") ] }
# Production logs to STDOUT, which is this script's output: keep the rescued errors out of it.
anonymous_probes = Rails.logger.silence(Logger::FATAL) { probe_paths.map { |verb, path| probe(session, verb, path) } }

puts JSON.pretty_generate(
  "generator" => "reference-tools/campfire/action_chains.rb",
  "rails" => Rails.version,
  "order" => "chain order: before and around callbacks run in this order, after callbacks in reverse order after the action; a before callback that renders or redirects halts the chain",
  "action_mailbox_ingress" => ActionMailbox.ingress&.to_s,
  "raise_on_missing_callback_actions" => ActionController::Base.raise_on_missing_callback_actions,
  "actions" => actions,
  "missing_controllers" => missing_controllers,
  "anonymous_probes" => anonymous_probes,
  "controllers" => controllers
)
