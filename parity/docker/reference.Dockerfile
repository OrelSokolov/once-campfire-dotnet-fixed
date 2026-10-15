# syntax = docker/dockerfile:1
#
# PARITY PATCH (see PROVENANCE.md, "Local adaptations"): reference/Dockerfile at 90b3300 with its
# Debian packages taken from one snapshot.debian.org date, so libvips, ffmpeg and everything they
# link are the exact builds the .NET image installs. The upstream file
# installs `libvips` and `ffmpeg` unpinned from deb.debian.org, which drops a version at the next
# point release. The differences from reference/Dockerfile, all in the base stage:
#
#   * FROM pins ruby:3.4.10-slim by its multi-arch index digest. That image was built from the
#     same snapshot date (its /etc/apt/sources.list.d/debian.sources names it).
#   * The apt sources point at snapshot.debian.org/archive/{debian,debian-security}/DEBIAN_SNAPSHOT
#     before the first apt-get update, and apt accepts the snapshot's expired Release files.
#     The build stage and the final stage both start FROM base, so every apt-get in this file and
#     in parity/docker/Dockerfile resolves against the snapshot.
#
# Everything else is reference/Dockerfile byte for byte. parity/bin/reference checks that file's
# SHA-256 (REFERENCE_DOCKERFILE_SHA256 there) and refuses to build when the submodule's Dockerfile
# changed, so this copy cannot drift silently.
#
# Built by `parity/bin/reference build` with reference/ as the build context.

# Make sure it matches the Ruby version in .ruby-version and Gemfile
ARG RUBY_VERSION=3.4.10
ARG RUBY_IMAGE_DIGEST=sha256:b573616eed67613e1d380ebb777d87aa094fad7505bdbc2d1b45dca9dd9116b9
FROM docker.io/library/ruby:$RUBY_VERSION-slim@$RUBY_IMAGE_DIGEST AS base

# Rails app lives here
WORKDIR /rails

# PARITY PATCH: one snapshot.debian.org date for every package.
ARG DEBIAN_SNAPSHOT=20260918T000000Z
RUN sed -i \
      -e "s|^URIs: http://deb.debian.org/debian-security\$|URIs: http://snapshot.debian.org/archive/debian-security/${DEBIAN_SNAPSHOT}|" \
      -e "s|^URIs: http://deb.debian.org/debian\$|URIs: http://snapshot.debian.org/archive/debian/${DEBIAN_SNAPSHOT}|" \
      /etc/apt/sources.list.d/debian.sources && \
    ! grep -q deb.debian.org /etc/apt/sources.list.d/debian.sources && \
    printf '%s\n' 'Acquire::Check-Valid-Until "false";' 'Acquire::Retries "5";' > /etc/apt/apt.conf.d/99parity-snapshot

# Install base packages
RUN apt-get update -qq && \
    apt-get install --no-install-recommends -y curl libsqlite3-0 libvips libjemalloc2 ffmpeg redis && \
    ln -s /usr/lib/$(uname -m)-linux-gnu/libjemalloc.so.2 /usr/local/lib/libjemalloc.so && \
    rm -rf /var/lib/apt/lists /var/cache/apt/archive

# Set production environment
ENV RAILS_ENV="production" \
    BUNDLE_DEPLOYMENT="1" \
    BUNDLE_PATH="/usr/local/bundle" \
    BUNDLE_WITHOUT="development" \
    LD_PRELOAD="/usr/local/lib/libjemalloc.so"


# Throw-away build stage to reduce size of final image
FROM base AS build

# Install packages need to build gems
RUN apt-get update -qq && \
    apt-get install -y build-essential git pkg-config libyaml-dev libssl-dev && \
    rm -rf /var/lib/apt/lists /var/cache/apt/archives

# Install application gems
COPY Gemfile Gemfile.lock vendor ./

RUN bundle install && \
    rm -rf ~/.bundle/ "${BUNDLE_PATH}"/ruby/*/cache "${BUNDLE_PATH}"/ruby/*/bundler/gems/*/.git

# Copy application code
COPY . .

# Precompiling assets for production without requiring secret RAILS_MASTER_KEY
RUN SECRET_KEY_BASE_DUMMY=1 ./bin/rails assets:precompile


# Final stage for app image
FROM base

# Image metadata
ARG OCI_DESCRIPTION
LABEL org.opencontainers.image.description="${OCI_DESCRIPTION}"
ARG OCI_SOURCE
LABEL org.opencontainers.image.source="${OCI_SOURCE}"
LABEL org.opencontainers.image.licenses="MIT"

# Run and own only the runtime files as a non-root user for security
RUN groupadd --system --gid 1000 rails && \
    useradd rails --uid 1000 --gid 1000 --create-home --shell /bin/bash
USER 1000:1000

# Configure environment defaults
ENV HTTP_IDLE_TIMEOUT=60
ENV HTTP_READ_TIMEOUT=300
ENV HTTP_WRITE_TIMEOUT=300

# Copy built artifacts: gems, application
COPY --from=build --chown=rails:rails /usr/local/bundle /usr/local/bundle
COPY --from=build --chown=rails:rails /rails /rails

# Install ONCE backup/restore hooks
COPY --chmod=755 hooks /hooks

# Set version and revision
ARG APP_VERSION
ENV APP_VERSION=$APP_VERSION
ARG GIT_REVISION
ENV GIT_REVISION=$GIT_REVISION

# Expose ports for HTTP and HTTPS
EXPOSE 80 443

# Start the server by default, this can be overwritten at runtime
CMD ["bin/boot"]
