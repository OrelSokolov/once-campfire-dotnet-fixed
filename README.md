# Campfire in C#

A C# port of [ONCE Campfire](https://github.com/basecamp/once-campfire), the self-hosted team chat app from Basecamp. The port uses ASP.NET Core Minimal APIs and native platform security. Rails is the feature reference, and the port uses the same browser frontend as Rails.

The port runs on .NET 11 RC2. The default Docker image is Native AOT, for Linux amd64 and arm64. A JIT image builds from the same source. Kestrel serves public HTTP and TLS in the application process. Redis and Resque are not necessary.

## Performance

We measured each app with 16 concurrent connections on a shared desktop with a 16-core processor. Each app had four CPU threads. Each value is the median of three interleaved runs on October 15, 2026. Each app ran its current default branch, with gzip and its default caches.

The C# column shows the JIT build, which was the faster of the two C# builds. The detailed results also give the values for Native AOT. Each page result is a repeated read of one page by one signed-in user. As a result, C# serves these pages from its warm response cache.

| HTTP workload (requests/sec) | [Rails](https://github.com/basecamp/once-campfire) | [Elixir](https://github.com/basecamp/once-campfire-elixir) | [Express](https://github.com/basecamp/once-campfire-express) | [Go](https://github.com/basecamp/once-campfire-go) | [Rust](https://github.com/basecamp/once-campfire-rust) | [C](https://github.com/basecamp/once-campfire-c) | C# |
|---|---:|---:|---:|---:|---:|---:|---:|
| Room page | 3,525 | 4,087 | 27,993 | 38,943 | 60,117 | 74,642 | 114,177 |
| Messages page | 3,577 | 4,203 | 41,314 | 39,425 | 58,924 | 74,385 | 122,813 |
| Sidebar | 3,782 | 4,296 | 54,931 | 44,072 | 65,872 | 81,306 | 130,994 |
| Search | 3,805 | 4,248 | 48,600 | 43,846 | 65,707 | 79,188 | 126,939 |
| Post a message | 406 | 802 | 864 | 1,660 | 2,346 | — | 2,506 |

The load generator validates every response and every post. We measured C for pages only. Elixir uses archived Debian packages.

Other Campfire READMEs show different absolute values, because they use different hardware and a different load generator. For example, the Rust README gives 106,494 requests/sec for the Rust room page, and this table gives 60,117. Values from different tables are not comparable. For the page workloads, the order of the apps is the same in both tables.

**Where C# falls behind:**

- **Memory:** C# uses 133 MiB when idle. This is about three times the memory of Rust, C and Go (37–44 MiB). But for this load, it is still a small amount of memory. In the page runs at 16 and 64 connections, the sampled peak for C# is about 200 MiB at more than 100,000 pages per second. This value includes the response cache, which can use up to 64 MiB. In the same runs, Rails, Express and Elixir have peaks of 356–1,016 MiB.
- **Posting:** Rust uses about 24% less CPU per post, and Go uses about 33% less. Rust and Go also have a lower median latency for a post. C# has the highest median posting rate, but its range across runs overlaps the range of Rust. C# also has the lowest p95 and p99 latency.
- **Page tail latency:** At 16 connections, C has a lower p99 latency than C# (0.40–0.43 ms, against 0.52–0.60 ms for C#).
- **Real-time updates to 10,000 clients** (Native AOT build only): At a steady pace, Rust uses about 40% less CPU per delivery and has a lower latency. When idle, Rust uses about a quarter of the memory of C#. At full load, the sampled peak for C# is about 1 GiB. This is similar to Go, less than Express, and much less than Rails and Elixir (6.5–7 GiB). Go and Elixir have only one valid full-load run each. We excluded the full-load result for Rust because a validation connection was lost.

**[Detailed results](docs/benchmarks.md):** 64 connections, CPU per request, latency, memory, real-time delivery, ranges, revisions and exclusions.

## Build options

Native AOT is the default image, using `OptimizationPreference=Speed` and the standard CPU baseline. JIT is supported from the same source and uses less CPU on page hits. Both set `System.Threading.ThreadPool.UnfairSemaphoreSpinLimit=256`, restoring RC1's default after RC2 raised it to 1,024. AOT retains its smaller memory footprint, startup behaviour and self-contained deployment. Static PGO and an AVX2 minimum CPU baseline are not adopted.

## Build and run

Initialize the pinned Rails reference, which supplies the browser assets:

```sh
git submodule update --init reference
docker build -t campfire-dotnet:aot .
docker build --target jit -t campfire-dotnet:jit .
```

For local HTTP, mount persistent storage, including its Data Protection keys:

```sh
docker run --rm -p 8080:80 -e DISABLE_SSL=1 \
  -v campfire-storage:/rails/storage campfire-dotnet:aot
```

Open http://localhost:8080 to create the first account. Preserve the storage volume and its `keys/` directory across restarts. This rewrite is for fresh installations; existing Rails credentials and protected URLs are not supported.

For HTTPS, set `TLS_DOMAIN`, expose ports 80 and 443 and retain the storage volume. Read [deployment and limitations](docs/deployment.md) before deployment, especially the backup and trusted-proxy settings.

## Development

Use the SDK pinned in [global.json](global.json). Install the repository's portable formatting preset locally before running the development gate:

```sh
cp eng/style.editorconfig .editorconfig
dotnet eng/check
```

See [development and testing](docs/development.md), [compatibility](docs/compatibility.md) and [contribution guidance](CONTRIBUTING.md). Imported sources, licenses and adaptations are recorded in [PROVENANCE.md](PROVENANCE.md).
