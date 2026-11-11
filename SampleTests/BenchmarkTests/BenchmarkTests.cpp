// Google Benchmark benchmarks with the different forms of benchmark names, failing, skipping, and crashing benchmarks,
// and counters and labels (see GTA's support of Google Benchmark)

#include <chrono>
#include <cstdlib>
#include <string>
#include <thread>
#include <vector>

#include "benchmark/benchmark.h"

static void BM_Simple(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(std::string("simple"));
}
BENCHMARK(BM_Simple);
// a custom name with the special characters of regular expressions
BENCHMARK(BM_Simple)->Name("BM_Custom.name(1+2)*[3]{4}|^$\\");

static void BM_Args(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(std::vector<char>(static_cast<size_t>(state.range(0))));
}
BENCHMARK(BM_Args)->Arg(8)->Arg(64);

static void BM_NamedArgs(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(state.range(0) + state.range(1));
}
BENCHMARK(BM_NamedArgs)->ArgNames({"size", "inserts"})->Args({100, 128});

static void BM_Capture(benchmark::State& state, int value) {
  for (auto _ : state)
    benchmark::DoNotOptimize(value);
}
BENCHMARK_CAPTURE(BM_Capture, answer, 42);

template <typename T>
static void BM_Template(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(T());
}
BENCHMARK_TEMPLATE(BM_Template, int);

class MyFixture : public benchmark::Fixture {};
BENCHMARK_F(MyFixture, FixtureMethod)(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(state.iterations());
}

namespace benchmarks {
  static void BM_InNamespace(benchmark::State& state) {
    for (auto _ : state)
      benchmark::DoNotOptimize(state.iterations());
  }
}
BENCHMARK(benchmarks::BM_InNamespace);

static void BM_Threads(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(state.thread_index());
}
BENCHMARK(BM_Threads)->Threads(2);

static void BM_RealTime(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(state.iterations());
}
BENCHMARK(BM_RealTime)->UseRealTime();

static void BM_CountersAndLabel(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(state.iterations());
  state.counters["Items"] = 42;
  state.counters["Rate"] = benchmark::Counter(static_cast<double>(state.iterations()), benchmark::Counter::kIsRate);
  state.SetLabel("my label");
}
BENCHMARK(BM_CountersAndLabel);

static void BM_Error(benchmark::State& state) {
  state.SkipWithError("Benchmark error");
  for (auto _ : state) {}
}
BENCHMARK(BM_Error);

static void BM_Skip(benchmark::State& state) {
  state.SkipWithMessage("Skipped by benchmark");
  for (auto _ : state) {}
}
BENCHMARK(BM_Skip);

// hangs only if environment variable GTA_BENCHMARK_HANG is set, so that the other benchmarks can be run together
static void BM_Hang(benchmark::State& state) {
  if (std::getenv("GTA_BENCHMARK_HANG") != nullptr)
    std::this_thread::sleep_for(std::chrono::seconds(30));
  for (auto _ : state)
    benchmark::DoNotOptimize(state.iterations());
}
BENCHMARK(BM_Hang);

// crashes only if environment variable GTA_BENCHMARK_CRASH is set, so that the other benchmarks can be run together
static void BM_Crash(benchmark::State& state) {
  if (std::getenv("GTA_BENCHMARK_CRASH") != nullptr) {
    volatile int* null = nullptr;
    *null = 42;
  }
  for (auto _ : state)
    benchmark::DoNotOptimize(state.iterations());
}
BENCHMARK(BM_Crash);

static void BM_AfterCrash(benchmark::State& state) {
  for (auto _ : state)
    benchmark::DoNotOptimize(state.iterations());
}
BENCHMARK(BM_AfterCrash);

BENCHMARK_MAIN();
