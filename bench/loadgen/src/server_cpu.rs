// Linux /proc accounting around one HTTP workload. The harness supplies only its owned
// container's cgroup; no unrelated process is sampled. Discovery and JSON work are outside
// the measured request interval. CPU time includes every process in the container.
use std::collections::BTreeMap;
use std::fs;
use std::io;

use serde_json::{Value, json};

// Linux USER_HZ, verified by getconf CLK_TCK on the benchmark host.
const TICKS_PER_SECOND: f64 = 100.0;

struct Process {
    name: String,
    start_ticks: u64,
    cpu_ticks: u64,
}

pub struct Snapshot {
    path: String,
    processes: BTreeMap<u32, Process>,
}

pub struct Sample {
    processes: Vec<(u32, String, u64)>,
}

fn invalid(message: &str) -> io::Error {
    io::Error::new(io::ErrorKind::InvalidData, message)
}

fn parse_stat(stat: &str) -> io::Result<Process> {
    let (prefix, suffix) = stat
        .rsplit_once(')')
        .ok_or_else(|| invalid("invalid process stat"))?;
    let (_, name) = prefix
        .split_once('(')
        .ok_or_else(|| invalid("missing process name"))?;
    let fields: Vec<&str> = suffix.split_whitespace().collect();
    let number = |index: usize| -> io::Result<u64> {
        fields
            .get(index)
            .ok_or_else(|| invalid("short process stat"))?
            .parse()
            .map_err(|_| invalid("non-numeric process stat"))
    };
    Ok(Process {
        name: name.to_string(),
        start_ticks: number(19)?,
        cpu_ticks: number(11)? + number(12)?,
    })
}

impl Snapshot {
    pub fn read(path: &str) -> io::Result<Self> {
        let mut processes = BTreeMap::new();
        for line in fs::read_to_string(format!("{path}/cgroup.procs"))?.lines() {
            let pid: u32 = line.parse().map_err(|_| invalid("invalid cgroup PID"))?;
            let process = parse_stat(&fs::read_to_string(format!("/proc/{pid}/stat"))?)?;
            processes.insert(pid, process);
        }
        if processes.is_empty() {
            return Err(invalid("server cgroup has no processes"));
        }
        Ok(Self {
            path: path.to_string(),
            processes,
        })
    }

    pub fn finish(self) -> io::Result<Sample> {
        let end = Self::read(&self.path)?;
        // A worker replacement would lose exited-process CPU. Reject the observation
        // instead of publishing an understated per-request number.
        if self.processes.keys().ne(end.processes.keys()) {
            return Err(invalid(
                "server process set changed during HTTP CPU measurement",
            ));
        }
        let mut processes = Vec::new();
        for (pid, first) in self.processes {
            let last = &end.processes[&pid];
            if first.start_ticks != last.start_ticks || last.cpu_ticks < first.cpu_ticks {
                return Err(invalid(
                    "server process was replaced during HTTP CPU measurement",
                ));
            }
            processes.push((pid, first.name, last.cpu_ticks - first.cpu_ticks));
        }
        Ok(Sample { processes })
    }
}

impl Sample {
    pub fn json(self, responses: u64) -> Value {
        let total: u64 = self.processes.iter().map(|(_, _, ticks)| ticks).sum();
        let per_response = |ticks: u64| -> Option<f64> {
            (responses > 0).then(|| ticks as f64 * 1000.0 / TICKS_PER_SECOND / responses as f64)
        };
        json!({
            "scope": "whole container, user + system CPU; completed HTTP responses",
            "ticks_per_second": TICKS_PER_SECOND,
            "cpu_secs": total as f64 / TICKS_PER_SECOND,
            "cpu_ms_per_response": per_response(total),
            "processes": self.processes.into_iter().map(|(pid, name, ticks)| json!({
                "pid": pid, "name": name, "cpu_secs": ticks as f64 / TICKS_PER_SECOND,
                "cpu_ms_per_response": per_response(ticks),
            })).collect::<Vec<_>>(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn stat_with_parentheses_in_the_process_name_uses_the_right_kernel_fields() {
        let stat = "17 (a (worker)) S 1 2 3 4 5 6 7 8 9 10 123 456 14 15 16 17 18 19 789 21";
        let process = parse_stat(stat).unwrap();
        assert_eq!(process.name, "a (worker)");
        assert_eq!(process.cpu_ticks, 579);
        assert_eq!(process.start_ticks, 789);
        assert!(parse_stat("17 (truncated) S 1").is_err());
    }

    #[test]
    fn aggregate_keeps_every_process_and_uses_completed_response_count() {
        let sample = Sample {
            processes: vec![(1, "thrust".into(), 10), (2, "campfire".into(), 90)],
        };
        let json = sample.json(200);
        assert_eq!(json["cpu_secs"], 1.0);
        assert_eq!(json["cpu_ms_per_response"], 5.0);
        assert_eq!(json["processes"][0]["cpu_ms_per_response"], 0.5);
    }

    #[test]
    fn a_reused_pid_invalidates_the_measurement() {
        let pid = std::process::id();
        let directory = std::env::temp_dir().join(format!("campfire-cpu-proof-{pid}"));
        fs::create_dir(&directory).unwrap();
        fs::write(directory.join("cgroup.procs"), pid.to_string()).unwrap();
        let path = directory.to_str().unwrap();
        let mut first = Snapshot::read(path).unwrap();
        first.processes.get_mut(&pid).unwrap().start_ticks += 1;
        let error = first.finish().err().expect("replaced PID must be rejected");
        assert!(error.to_string().contains("process was replaced"));
        fs::remove_file(directory.join("cgroup.procs")).unwrap();
        fs::remove_dir(directory).unwrap();
    }
}
