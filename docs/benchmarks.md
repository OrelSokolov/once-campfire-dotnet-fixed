# Benchmark results

Results from October 15, 2026. Upstream default branches were checked and pinned for the complete comparison; C uses `master`. Application sources are unmodified upstream sources. Elixir uses archived Debian packages, with its application, dependencies and runtime configuration unchanged.

Three interleaved repetitions in one session on a **shared desktop with a 16-core processor**. Applications use four CPU threads; generators use eight separate threads, including their SMT siblings. Application siblings are monitored. Starts require one-minute load average below 1.5, with up to 900 seconds of waiting. Timing has no runtime tracing or debug logging. Desktop activity and loopback copying remain sources of noise.

Pages use gzip at 16 and 64 connections, a five-second warm-up and 20-second windows. Posting uses 16 connections, a 120-second warm-up and a 120-second window. Cable uses 10,000 clients, 1,000 paced posts at 10/s, then 120 seconds of saturation with four posters, followed by a bounded drain. Rust main is paced-only. There are no identity-page, 1k Cable, c64 posting or JIT Cable measurements in this session.

Every HTTP body is validated: each distinct body is fully checked, and repeats are recognized by exact bytes. Every post is independently verified as persisted exactly once. Every Cable delivery retains missing, duplicate, off-stream and independent decoded-text checks. Invalid samples contribute no numbers. Every result passed the CPU limit for the load generator. The highest generator utilization was 0.38, and the limit was 0.85. Sample counts below three are shown explicitly.

The tools for these validation and persistence checks are not in this repository. The tools that ran Go, Express and Elixir are also not in this repository. The checked-in `bench/run` script runs only the Rails, Rust, C and C# targets.

C# page hits use a generation-keyed response cache for each identity, with a 64 MiB budget. Changes to sessions and permissions stop the reuse. Upstream cache policies remain their defaults, including Express's separate cache per worker. Each implementation runs these workloads with its own caching, rendering, authentication, security and compression policies. As a result, the tables do not show equal feature coverage. They also do not isolate the cost of a language or a runtime. At c64, both C# builds use less server CPU per page than every included reference on all four routes. Rust main and Go use less CPU per post, and Rust main uses less CPU and memory for paced Cable delivery.

Memory is summed process PSS, including child workers and required Redis within the application's cgroup. HTTP body bytes are encoded bodies. Cable bytes include received RFC 6455 frames and companion streams, excluding the HTTP handshake, per logical message delivery. Deliveries/s is the generator's `frames_per_sec`: one message delivered to one subscriber. Fully delivered broadcasts/s is a different metric. Peaks are sampled and can miss brief spikes.

## Pages

Medians of three valid repetitions; ranges are min–max. CPU is application user plus system CPU per response.

| Target | Route | c | Responses/s [range] | CPU µs [range] | p50 / p95 / p99 ms | Idle / peak PSS MiB | Body bytes | Max generator util |
|---|---|---:|---|---|---|---|---:|---:|
| C# AOT | room | 16 | 87,429 [86,276–88,422] | 34.78 [34.75–35.34] | 0.155 / 0.359 / 0.711 | 119.5 / 140.4 | 23460 | 0.282 |
| C# AOT | messages | 16 | 93,813 [93,284–94,025] | 32.71 [32.24–33.14] | 0.146 / 0.318 / 0.649 | 119.5 / 135.3 | 14040 | 0.285 |
| C# AOT | sidebar | 16 | 98,759 [98,494–100,452] | 30.76 [30.09–31.34] | 0.138 / 0.299 / 0.599 | 119.5 / 140.1 | 6684 | 0.299 |
| C# AOT | search | 16 | 96,269 [94,729–97,562] | 31.64 [31.35–32.28] | 0.142 / 0.311 / 0.602 | 119.5 / 145.6 | 9918 | 0.294 |
| C# AOT | room | 64 | 105,012 [104,851–107,362] | 31.50 [31.33–32.02] | 0.528 / 1.180 / 1.829 | 119.5 / 144.0 | 23989 | 0.304 |
| C# AOT | messages | 64 | 114,093 [110,358–114,441] | 29.22 [29.18–29.52] | 0.497 / 1.079 / 1.600 | 119.5 / 140.6 | 14704 | 0.304 |
| C# AOT | sidebar | 64 | 120,165 [115,684–120,619] | 27.46 [27.40–27.77] | 0.470 / 1.032 / 1.513 | 119.5 / 145.1 | 6736 | 0.308 |
| C# AOT | search | 64 | 116,592 [112,247–118,811] | 28.57 [28.14–28.65] | 0.483 / 1.076 / 1.562 | 119.5 / 152.2 | 9986 | 0.299 |
| C# JIT | room | 16 | 114,177 [111,792–115,393] | 24.85 [24.01–25.46] | 0.113 / 0.307 / 0.603 | 133.2 / 177.0 | 23448 | 0.339 |
| C# JIT | messages | 16 | 122,813 [122,075–124,202] | 23.13 [22.49–23.40] | 0.105 / 0.267 / 0.567 | 133.2 / 187.3 | 14025 | 0.343 |
| C# JIT | sidebar | 16 | 130,994 [130,715–131,963] | 21.46 [21.15–21.47] | 0.099 / 0.245 / 0.517 | 133.2 / 191.4 | 6686 | 0.356 |
| C# JIT | search | 16 | 126,939 [125,806–127,393] | 22.02 [22.00–22.46] | 0.103 / 0.253 / 0.548 | 133.2 / 195.3 | 9921 | 0.353 |
| C# JIT | room | 64 | 134,580 [133,820–136,274] | 22.70 [22.50–22.81] | 0.413 / 0.948 / 1.445 | 133.2 / 190.7 | 23984 | 0.379 |
| C# JIT | messages | 64 | 143,973 [143,875–145,264] | 21.20 [21.10–21.26] | 0.392 / 0.861 / 1.299 | 133.2 / 194.0 | 14702 | 0.366 |
| C# JIT | sidebar | 64 | 152,963 [151,509–153,967] | 19.63 [19.60–19.97] | 0.370 / 0.831 / 1.238 | 133.2 / 192.2 | 6739 | 0.371 |
| C# JIT | search | 64 | 147,945 [146,723–148,492] | 20.43 [20.00–20.60] | 0.380 / 0.855 / 1.267 | 133.2 / 198.4 | 9990 | 0.376 |
| Rails main | room | 16 | 3,525 [3,254–3,624] | 937.91 [913.71–1,055.09] | 3.870 / 9.508 / 13.083 | 441.6 / 779.9 | 20754 | 0.163 |
| Rails main | messages | 16 | 3,577 [3,543–3,615] | 923.00 [918.66–924.54] | 3.929 / 9.177 / 13.001 | 441.6 / 813.6 | 11785 | 0.029 |
| Rails main | sidebar | 16 | 3,782 [3,699–3,968] | 854.88 [843.03–860.96] | 3.727 / 8.598 / 11.872 | 441.6 / 847.2 | 5880 | 0.027 |
| Rails main | search | 16 | 3,805 [3,742–3,917] | 864.53 [853.76–866.40] | 3.686 / 8.558 / 11.909 | 441.6 / 866.8 | 9374 | 0.030 |
| Rails main | room | 64 | 3,583 [3,425–3,603] | 945.13 [933.98–1,024.84] | 15.061 / 39.774 / 60.037 | 441.6 / 811.0 | 20754 | 0.140 |
| Rails main | messages | 64 | 3,702 [3,639–3,769] | 917.74 [906.92–921.56] | 15.056 / 35.419 / 51.234 | 441.6 / 840.4 | 11785 | 0.032 |
| Rails main | sidebar | 64 | 3,955 [3,859–4,132] | 851.56 [837.51–858.56] | 11.650 / 49.123 / 67.431 | 441.6 / 859.4 | 5880 | 0.030 |
| Rails main | search | 64 | 3,962 [3,927–4,078] | 862.90 [847.90–863.87] | 12.320 / 45.049 / 65.717 | 441.6 / 879.9 | 9374 | 0.034 |
| Rust main | room | 16 | 60,117 [59,595–60,154] | 53.17 [52.84–53.18] | 0.202 / 0.635 / 1.066 | 43.9 / 68.8 | 24286 | 0.201 |
| Rust main | messages | 16 | 58,924 [58,786–59,569] | 54.69 [53.92–54.79] | 0.203 / 0.656 / 1.137 | 43.9 / 95.5 | 16158 | 0.198 |
| Rust main | sidebar | 16 | 65,872 [65,694–65,964] | 48.05 [47.80–48.14] | 0.183 / 0.577 / 0.959 | 43.9 / 93.4 | 5911 | 0.211 |
| Rust main | search | 16 | 65,707 [65,626–65,843] | 47.98 [47.75–48.13] | 0.184 / 0.575 / 0.943 | 43.9 / 94.2 | 9755 | 0.214 |
| Rust main | room | 64 | 78,890 [78,668–79,509] | 44.75 [44.53–45.13] | 0.378 / 3.074 / 5.162 | 43.9 / 96.3 | 24286 | 0.258 |
| Rust main | messages | 64 | 77,968 [77,883–77,976] | 45.63 [45.62–45.95] | 0.379 / 3.151 / 5.329 | 43.9 / 100.3 | 16158 | 0.247 |
| Rust main | sidebar | 64 | 88,416 [88,353–88,468] | 39.84 [39.80–40.03] | 0.325 / 2.814 / 4.849 | 43.9 / 99.8 | 5911 | 0.267 |
| Rust main | search | 64 | 88,119 [87,861–88,176] | 39.85 [39.79–40.16] | 0.323 / 2.836 / 4.888 | 43.9 / 100.4 | 9755 | 0.267 |
| C master | room | 16 | 74,642 [74,619–75,447] | 46.40 [46.02–46.41] | 0.209 / 0.331 / 0.428 | 40.7 / 42.9 | 21303 | 0.239 |
| C master | messages | 16 | 74,385 [74,190–75,669] | 46.37 [45.49–46.51] | 0.211 / 0.328 / 0.421 | 40.7 / 45.6 | 12311 | 0.229 |
| C master | sidebar | 16 | 81,306 [80,922–82,049] | 42.70 [42.13–42.81] | 0.190 / 0.304 / 0.396 | 40.7 / 45.8 | 5900 | 0.248 |
| C master | search | 16 | 79,188 [79,007–80,639] | 43.98 [43.05–44.00] | 0.195 / 0.311 / 0.405 | 40.7 / 46.1 | 9525 | 0.246 |
| C master | room | 64 | 92,176 [92,030–92,590] | 37.94 [37.85–38.08] | 0.670 / 1.148 / 1.627 | 40.7 / 43.1 | 21303 | 0.269 |
| C master | messages | 64 | 93,362 [92,065–93,586] | 37.46 [37.37–37.92] | 0.660 / 1.137 / 1.667 | 40.7 / 45.7 | 12311 | 0.262 |
| C master | sidebar | 64 | 97,105 [96,543–97,415] | 35.90 [35.87–36.20] | 0.656 / 1.053 / 1.536 | 40.7 / 45.8 | 5900 | 0.271 |
| C master | search | 64 | 94,574 [94,554–94,877] | 36.92 [36.88–36.99] | 0.676 / 1.077 / 1.587 | 40.7 / 46.1 | 9525 | 0.270 |
| Go main | room | 16 | 38,943 [38,380–39,253] | 92.42 [91.70–93.57] | 0.302 / 1.079 / 1.660 | 37.4 / 49.5 | 21333 | 0.182 |
| Go main | messages | 16 | 39,425 [39,372–39,564] | 91.36 [91.23–91.86] | 0.299 / 1.059 / 1.616 | 37.4 / 51.2 | 12940 | 0.181 |
| Go main | sidebar | 16 | 44,072 [43,330–44,177] | 81.10 [81.00–82.26] | 0.266 / 0.959 / 1.505 | 37.4 / 52.1 | 5816 | 0.194 |
| Go main | search | 16 | 43,846 [43,741–44,034] | 82.26 [81.87–82.46] | 0.271 / 0.944 / 1.467 | 37.4 / 53.0 | 9432 | 0.195 |
| Go main | room | 64 | 40,702 [40,387–40,984] | 89.08 [88.27–89.70] | 1.385 / 3.329 / 4.669 | 37.4 / 52.8 | 21333 | 0.191 |
| Go main | messages | 64 | 41,341 [41,086–41,370] | 87.94 [87.82–88.48] | 1.364 / 3.285 / 4.594 | 37.4 / 54.8 | 12940 | 0.189 |
| Go main | sidebar | 64 | 46,195 [45,712–46,241] | 78.03 [77.98–78.89] | 1.101 / 3.564 / 5.134 | 37.4 / 55.5 | 5816 | 0.202 |
| Go main | search | 64 | 46,000 [45,912–46,068] | 78.88 [78.84–79.20] | 1.106 / 3.556 / 5.114 | 37.4 / 56.2 | 9432 | 0.204 |
| Express main | room | 16 | 27,993 [21,591–28,641] | 136.17 [133.98–136.72] | 0.429 / 0.967 / 1.783 | 306.8 / 1005.4 | 19522 | 0.109 |
| Express main | messages | 16 | 41,314 [41,143–41,469] | 91.73 [91.24–92.00] | 0.373 / 0.669 / 1.037 | 306.8 / 1015.0 | 11006 | 0.147 |
| Express main | sidebar | 16 | 54,931 [54,922–55,473] | 68.39 [67.65–68.53] | 0.281 / 0.643 / 0.733 | 306.8 / 1006.1 | 5424 | 0.189 |
| Express main | search | 16 | 48,600 [37,352–49,200] | 76.91 [76.58–77.55] | 0.315 / 0.620 / 0.996 | 306.8 / 1006.8 | 8990 | 0.171 |
| Express main | room | 64 | 28,414 [28,317–28,484] | 134.81 [134.45–135.04] | 2.177 / 3.514 / 5.142 | 306.8 / 1011.0 | 19522 | 0.114 |
| Express main | messages | 64 | 41,212 [40,995–41,542] | 91.97 [91.10–92.22] | 1.468 / 2.545 / 3.197 | 306.8 / 1015.7 | 11006 | 0.153 |
| Express main | sidebar | 64 | 55,753 [55,439–56,035] | 67.44 [67.06–67.88] | 1.189 / 1.704 / 2.952 | 306.8 / 1006.1 | 5424 | 0.193 |
| Express main | search | 64 | 49,353 [48,751–49,424] | 76.42 [76.25–77.40] | 1.342 / 1.732 / 2.871 | 306.8 / 1007.0 | 8990 | 0.176 |
| Elixir (archived Debian) | room | 16 | 4,087 [4,085–4,105] | 923.02 [918.79–923.24] | 3.605 / 6.803 / 8.881 | 236.6 / 299.4 | 20676 | 0.038 |
| Elixir (archived Debian) | messages | 16 | 4,203 [4,182–4,227] | 898.49 [891.55–899.83] | 3.513 / 6.609 / 8.703 | 236.6 / 302.0 | 11908 | 0.037 |
| Elixir (archived Debian) | sidebar | 16 | 4,296 [4,290–4,314] | 878.18 [874.36–880.63] | 3.443 / 6.385 / 8.336 | 236.6 / 300.2 | 5888 | 0.031 |
| Elixir (archived Debian) | search | 16 | 4,248 [4,245–4,249] | 886.34 [885.95–888.03] | 3.475 / 6.476 / 8.472 | 236.6 / 302.0 | 9444 | 0.038 |
| Elixir (archived Debian) | room | 64 | 4,245 [4,217–4,248] | 888.42 [887.25–892.99] | 14.615 / 22.814 / 28.088 | 236.6 / 356.0 | 20676 | 0.043 |
| Elixir (archived Debian) | messages | 64 | 4,359 [4,355–4,371] | 864.08 [862.96–864.12] | 14.161 / 22.348 / 27.379 | 236.6 / 350.5 | 11908 | 0.042 |
| Elixir (archived Debian) | sidebar | 64 | 4,413 [4,401–4,420] | 854.02 [852.54–856.10] | 14.034 / 21.756 / 26.767 | 236.6 / 338.1 | 5888 | 0.033 |
| Elixir (archived Debian) | search | 64 | 4,348 [4,328–4,349] | 865.94 [865.85–869.14] | 14.256 / 21.994 / 27.095 | 236.6 / 335.4 | 9444 | 0.042 |

## Posting

Gzip, c16. All acknowledged posts were persisted exactly once.

| Target | n | Posts/s [range] | CPU µs/post [range] | p50 / p95 / p99 ms | Idle / peak PSS MiB | Body bytes | Max generator util |
|---|---:|---|---|---|---|---:|---:|
| C# AOT | 3 | 2,501 [2,493–2,505] | 908.75 [907.62–909.06] | 5.411 / 8.000 / 32.265 | 121.2 / 157.5 | 2070 | 0.036 |
| C# JIT | 3 | 2,506 [1,922–2,517] | 859.13 [857.01–862.73] | 5.278 / 8.124 / 33.140 | 135.4 / 211.3 | 2070 | 0.036 |
| Rails main | 3 | 406 [406–413] | 9,176.34 [9,049.56–9,195.79] | 35.193 / 77.447 / 98.565 | 453.7 / 978.9 | 2029 | 0.010 |
| Rust main | 3 | 2,346 [2,329–2,504] | 656.36 [654.46–663.49] | 3.643 / 14.506 / 50.573 | 43.8 / 193.0 | 2008 | 0.035 |
| Go main | 3 | 1,660 [1,651–1,716] | 579.15 [578.24–580.50] | 3.273 / 31.933 / 52.547 | 39.4 / 136.3 | 1947 | 0.025 |
| Express main | 3 | 864 [854–864] | 1,258.88 [1,247.68–1,269.02] | 9.897 / 62.309 / 138.519 | 309.9 / 7125.8 | 1903 | 0.018 |
| Elixir (archived Debian) | 3 | 802 [796–804] | 4,551.90 [4,547.14–4,581.55] | 17.619 / 33.080 / 41.948 | 236.0 / 363.1 | 2022 | 0.016 |

Rust main and Go use less CPU per post than either C# build. Express's posting peak is about 7,080–7,315 MiB PSS, with about 7,058 MiB anonymous memory; the high allocation is consistent across all three repetitions.

## Cable

10,000 clients. Latency is post-to-received-by-all, rather than one client. Negotiated modes are observed from upgrades. Plain-mode loopback copying can constrain delivery rates even below the generator CPU ceiling. Paced-only rows have no saturation estimate.

| Target | Mode | n | Saturated deliveries/s [range] | Saturated CPU µs/delivery | Paced CPU µs/delivery | Paced p50 / p95 / p99 ms | Saturated p99 ms | Idle / fan-out / drained PSS MiB | Connect s | Wire bytes/delivery |
|---|---|---:|---|---:|---:|---|---:|---|---:|---:|
| C# AOT | deflate | 3 | 534,687 [533,763–536,226] | 6.38 | 11.24 | 36.54 / 46.85 / 51.20 | 114.17 | 759.8 / 1045.4 / 709.7 | 1.41 | 2452 |
| Rust main | deflate; paced only | 3 | — | — | 6.96 | 22.73 / 28.19 / 33.92 | — | 190.2 / — / 198.2 | 1.26 | 2333 |
| Go main | deflate | 1 | 252,199 [252,199–252,199] | 10.25 | 13.19 | 35.81 / 65.66 / 77.89 | 1939.45 | 689.2 / 1060.7 / 1060.8 | 1.21 | 2658 |
| Rails main | plain | 2 | 28,562 [28,506–28,618] | 110.78 | 118.25 | 695.04 / 2220.03 / 2859.52 | 8140.80 | 2468.3 / 6994.7 / 6154.4 | 13.88 | 12002 |
| Express main | plain | 2 | 77,154 [76,595–77,713] | 46.10 | 52.12 | 308.35 / 394.62 / 434.18 | 3168.26 | 576.2 / 1336.6 / 1336.6 | 2.27 | 9196 |
| Elixir (archived Debian) | plain | 1 | 70,495 [70,495–70,495] | 43.73 | 38.48 | 85.18 / 130.37 / 139.13 | 58032.13 | 2176.5 / 6700.2 / 4552.3 | 3.70 | 11982 |

Rust main's paced CPU is about 6.96 versus C# AOT's 11.2 µs/delivery, with about a quarter of the idle PSS (190 versus 760 MiB). Its saturation result is excluded because a separate plain validation connection was lost in qualification; a slow-reader disconnect is a hypothesis, not a confirmed cause.

## Idle and loaded HTTP memory

Median fresh-process idle PSS and median of each repetition's maximum loaded PSS. Page peaks cover all eight route/concurrency windows.

| Target | Pages idle / peak PSS MiB | Posting idle / peak PSS MiB |
|---|---|---|
| C# AOT | 119.5 / 152.2 | 121.2 / 157.5 |
| C# JIT | 133.2 / 201.2 | 135.4 / 211.3 |
| Rails main | 441.6 / 879.9 | 453.7 / 978.9 |
| Rust main | 43.9 / 100.4 | 43.8 / 193.0 |
| C master | 40.7 / 46.1 | — |
| Go main | 37.4 / 56.2 | 39.4 / 136.3 |
| Express main | 306.8 / 1015.7 | 309.9 / 7125.8 |
| Elixir (archived Debian) | 236.6 / 356.0 | 236.0 / 363.1 |

The .NET response cache is bounded at 64 MiB. A full cache can raise peak AOT process memory by about 2–2.5 times its charge because Server GC retains committed memory. Compressed idle Cable adds about 1.6 KiB live memory and 4–8 KiB process memory per connection. These are separate diagnostic observations; PSS does not identify individual live objects.

## Build options

| Build | Settings |
|---|---|
| Native AOT, default image | `OptimizationPreference=Speed`; standard CPU baseline; ThreadPool spin limit 256 |
| JIT, supported target | Same source, ReadyToRun; ThreadPool spin limit 256 |

The spin limit restores .NET 11 RC1's default, raised to 1,024 in RC2. The tables above include this setting, but they do not isolate its effect. Static PGO and an AVX2 minimum CPU baseline are not adopted. Native AOT remains the default for its smaller memory footprint, startup behaviour and self-contained deployment; JIT remains supported from the same application source.

## Revisions and artifacts

Image IDs identify immutable local timing artifacts, rather than published release images. The C# images were built from the source before a later code cleanup. These tables do not include measurements of the code after this cleanup. The default JIT runtimeconfig carries the same spin limit of 256.

| Target | Revision | Image ID | Manifest digest |
|---|---|---|---|
| C# AOT | `source snapshot, .NET 11 RC2, Speed, spin 256` | `7006cab26004ecb071f9d1c3538156f84790f6d3aad77990b3f39d9446d013fb` | `sha256:92b6c9ec84a7e84b0c85c7f7d638265e08ac0eaf2513a9be5e0343a93597d1d0` |
| C# JIT | `source snapshot, .NET 11 RC2, ReadyToRun, spin 256` | `08443ff7c899483cc5fc2e18a0dde3f24de99daa25aecf5ee99ce51671997d0f` | `sha256:0cba7da7c485582eadfa559b3b566bacac2146a2399e122db3f9a11af3a3f947` |
| Rails main | `edbc779f4dfc9b26c36310881711ddc976a9dfc8` | `d92403621d2e33a2d936752f70a5f6b750cd7c010fa37cd8f154c8764d614c99` | `sha256:a4cfb982de97792be4bff8a52e47f2addb74f6ba872a4f535394be14a0717563` |
| Rust main | `6dae2fd3c1a2dce61ca11beb731bcce2b42b67ad` | `6b8f7b9346e5ae999a75b723c735dddef0c96d72a42e4946ac612b669442e366` | `sha256:8f44d7f5af5e15d1065c754186336e99562c55605ec402135cb574b0575b523a` |
| C master | `873a51f523de5a0cc235884e5725cfb5521d2027` | `7b9559ba342ba931c92629a3d9fa0cd3133dddc82a52daf6617f419419a5da81` | `sha256:2181cee3bcc60874a00c1621894057795c6026d39465edaa230d2be52db48d18` |
| Go main | `c4ab53d00b0a19a55962e03d18057ca6c3da9437` | `1b86a74051a22c22ad5986cd6dc0c40c2ce6a33ee6d92a8d317f15ec0f17a1e1` | `sha256:4ed4082304fc367d8da147847b43ff3d377d0317a77cd2a327e2396a35385058` |
| Express main | `bb70a7d478160c016393d981e3538987247d3b3d` | `7b25478bfa0ca97eb2457d5c3a1f9d5b500e9799136fef19b2552c73eba1fe49` | `sha256:2133b511a99e501124a24b7349d7fb86e98ca17fe3c7a0ef41c8acb56be11e2c` |
| Elixir (archived Debian) | `58964cf8a8788b2a20c7bde16dae65b025812593` | `64759b59e5aa34568d798571a59e1c2e3547bea701176571d4ea0a5f1aba532a` | `sha256:c276e621e4a4e00b132610154d6802959b160389fe0b2633b4751c83e4a820ea` |

## Limits and exclusions

- C is pages-only. No C posting or Cable figures are implied. JIT Cable and the older pinned Rails reference are unmeasured here.
- Go Cable has one valid repetition out of three. In the other two saturation runs, 5,357 and 8,552 connections ended with EOF without a close frame; those complete runs are excluded.
- Express Cable has two valid repetitions. Two posts timed out after 30 seconds in the excluded repetition. Express uses four workers with separate response caches.
- Rails Cable has two valid repetitions. One post timed out after 30 seconds in the excluded repetition. This capacity result does not establish actual message loss.
- Elixir Cable has one valid repetition. In the other two, 750,000 and 1,109,703 deliveries remained unobserved after the 60-second drain. This does not establish permanent loss. Elixir declines compression and is measured plain.
- Elixir is built with the exact archived Debian packages from snapshot `20260918T000000Z`: libvips-dev/libvips42t64 `8.16.1-1+deb13u1`, ffmpeg `7:7.1.5-0+deb13u1`. Only build package-source configuration changed; application code, dependencies and runtime configuration did not.
- Rails and Express decline Cable compression; their plain mode is shown. Rust main's 10k deflate saturation remains excluded, with its independently validated paced results shown.
- Every excluded attempt is retained separately. Successful-run medians at n=1 or n=2 are conditional on validation and do not describe reliability over all three attempts. No failed attempt is pooled, no reference timing is rerun to replace one, and no historical cohort supplies a missing row.
