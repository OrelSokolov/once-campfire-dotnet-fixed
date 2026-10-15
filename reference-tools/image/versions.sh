#!/usr/bin/env bash
# Prints the native and runtime versions inside a Campfire image, for generated runtime inventories.
#
#   PATH="$PWD/eng/docker-shim:$PATH" reference-tools/image/versions.sh campfire-reference:app
#
# The image must be the reference app image (Ruby, the bundle, Thruster). Output is Markdown-ish
# `name: value` lines followed by the full `dpkg-query` package list.
set -euo pipefail

image=${1:-campfire-reference:app}

docker run --rm -i --entrypoint /bin/bash "$image" -s <<'EOF'
set -euo pipefail
cd /rails
pkg() { dpkg-query -W -f='${Version}' "$1" 2>/dev/null || echo "(not installed)"; }
echo "debian_version: $(cat /etc/debian_version)"
grep -h '^URIs:' /etc/apt/sources.list.d/debian.sources | sed 's/^/apt_source: /'
for p in libvips42t64 ffmpeg libavcodec61 libsqlite3-0 redis-server libjemalloc2 curl ca-certificates libc6 libssl3t64 libheif1 libwebp7 libjpeg62-turbo libpng16-16t64 libtiff6 libexif12 liblcms2-2 libglib2.0-0t64 imagemagick poppler-utils mupdf-tools; do
  echo "dpkg $p: $(pkg "$p")"
done
echo "ruby: $(ruby -e 'print RUBY_DESCRIPTION')"
echo "bundler: $(bundle -v)"
echo "rails: $(bundle exec ruby -e 'require "rails"; print Rails.version')"
echo "sqlite3 gem: $(bundle exec ruby -e 'require "sqlite3"; print SQLite3::VERSION')"
echo "sqlite3 gem SQLITE_VERSION (bundled library): $(bundle exec ruby -e 'require "sqlite3"; print SQLite3::SQLITE_VERSION')"
echo "sqlite3 gem SQLITE_LOADED_VERSION: $(bundle exec ruby -e 'require "sqlite3"; print SQLite3::SQLITE_LOADED_VERSION')"
echo "sqlite3 gem compile options: $(bundle exec ruby -e 'require "sqlite3"; db = SQLite3::Database.new(":memory:"); print db.execute("PRAGMA compile_options").flatten.join(" ")')"
echo "ruby-vips gem: $(bundle exec ruby -e 'require "vips"; print Vips::VERSION')"
echo "libvips (loaded by ruby-vips): $(bundle exec ruby -e 'require "vips"; print Vips.version_string')"
echo "image_processing gem: $(bundle exec ruby -e 'require "image_processing"; print ImageProcessing::VERSION')"
echo "thruster gem: $(bundle exec ruby -e 'print Gem.loaded_specs["thruster"].version')"
echo "thrust binary: $(bundle exec thrust --version 2>&1 | head -1 || true)"
echo "thrust executable: $(find /usr/local/bundle -path '*thruster*/exe/*/thrust' -type f | head -1)"
echo "thrust sha256: $(sha256sum "$(find /usr/local/bundle -path '*thruster*/exe/*/thrust' -type f | head -1)" | cut -d' ' -f1)"
echo "redis-server: $(redis-server --version)"
echo "puma gem: $(bundle exec ruby -e 'print Gem.loaded_specs["puma"].version')"
echo "resque gem: $(bundle exec ruby -e 'print Gem.loaded_specs["resque"].version')"
echo "nokogiri: $(bundle exec ruby -e 'require "nokogiri"; print Nokogiri::VERSION, " libxml2 ", Nokogiri::VERSION_INFO.dig("libxml", "loaded"), " gumbo"')"
echo "ffmpeg: $(ffmpeg -hide_banner -version | head -1)"
echo "ffprobe: $(ffprobe -hide_banner -version | head -1)"
echo "jemalloc LD_PRELOAD: ${LD_PRELOAD:-}"
echo
echo "## dpkg-query -W"
dpkg-query -W -f='${Package} ${Version}\n'
EOF
