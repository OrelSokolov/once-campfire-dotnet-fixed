# Records every environment variable the reference reads while it boots, eager loads, and serves a
# few requests, with the first code location that read each one. Preloaded into Ruby, so reads by
# gems and by config/*.yml ERB count too:
#
#   parity/bin/reference exec --seed default -e RUBYOPT=-r/work/reference-tools/inventory/env_trace.rb \
#     -e ENV_TRACE_OUT=/work/env-trace.json -- \
#     bin/rails runner /work/reference-tools/inventory/env_trace_workload.rb
#
# Static reads that never run in that workload (config/puma.rb, bin/*, hooks, the Procfile, and
# Thruster's own variables) are listed separately in the selected output directoryenvironment.md.
require "json"

module EnvTrace
  OUT = ENV["ENV_TRACE_OUT"]
  READS = {}
  SELF = File.expand_path(__FILE__)

  def self.record(name, method)
    return unless name.is_a?(String) || name.is_a?(Symbol)
    name = name.to_s
    return if READS.key?(name)
    location = caller_locations(2, 20).find { |l| l.absolute_path != SELF && !l.path.start_with?("<internal") }
    READS[name] = { "method" => method.to_s, "first_read_at" => short(location&.path, location&.lineno) }
  end

  def self.short(path, line)
    return nil unless path
    path = path.sub(%r{\A/rails/}, "")
      .sub(%r{\A.*/bundler/gems/rails-[0-9a-f]+/}, "rails/")
      .sub(%r{\A.*/gems/}, "")
      .sub(%r{\A/usr/local/lib/ruby/}, "ruby/")
    "#{path}:#{line}"
  end

  module Tracing
    %i[[] fetch key? has_key? include? member? values_at dig].each do |method|
      define_method(method) do |*args, **opts, &block|
        # Only values_at takes several names; fetch's second argument is its default.
        (method == :values_at ? args : args.first(1)).each { |name| EnvTrace.record(name, method) }
        opts.empty? ? super(*args, &block) : super(*args, **opts, &block)
      end
    end
  end
end

ENV.singleton_class.prepend(EnvTrace::Tracing)

at_exit do
  if EnvTrace::OUT
    File.write(EnvTrace::OUT, JSON.pretty_generate(EnvTrace::READS.sort.to_h) + "\n")
  end
end
