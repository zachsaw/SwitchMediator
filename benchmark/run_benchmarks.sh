#!/bin/bash
set -e

# --- Configuration ---
BENCHMARK_PROJECT_DIR="./Mediator.Switch.Benchmark"
GENERATOR_PROJECT_DIR="./Mediator.Switch.Benchmark.Generator"
GENERATED_CODE_DIR_NAME="Generated"
OUTPUT_ARTIFACTS_BASE_DIR="./BenchmarkDotNet.Artifacts"

# Define N and B values to test
N_VALUES=(25 100 600)
B_VALUES=(1 5)

# Fixed values for cross-benchmarking
FIXED_N_FOR_PIPELINE_TEST=100 # Use N=100 when testing pipeline scaling (B)
FIXED_B_FOR_HANDLER_TEST=0   # Use B=0 when testing handler scaling (N)

BENCHMARK_PROJECT_FILE="$BENCHMARK_PROJECT_DIR/Mediator.Switch.Benchmark.csproj"
GENERATOR_PROJECT_FILE="$GENERATOR_PROJECT_DIR/Mediator.Switch.Benchmark.Generator.csproj"
GENERATED_CODE_FULL_PATH="$BENCHMARK_PROJECT_DIR/$GENERATED_CODE_DIR_NAME"
BUILD_CONFIG="Release"
# --- End Configuration ---

# --- Functions (print_*, run_command) remain the same ---
print_info() { printf "\e[36m%s\e[0m\n" "$1"; }
print_success() { printf "\e[32m%s\e[0m\n" "$1"; }
print_warning() { printf "\e[33m%s\e[0m\n" "$1"; }
print_error() { printf "\e[31m%s\e[0m\n" "$1" >&2; }
run_command() { print_info "Executing: $*"; "$@"; local status=$?; if [ $status -ne 0 ]; then print_error "Command failed: $*"; exit $status; fi; print_success "Command executed successfully."; }

print_warning "Starting Benchmark Orchestration..."
mkdir -p "$OUTPUT_ARTIFACTS_BASE_DIR"

# === Run Handler Scaling Benchmarks ===
print_warning "============================================="
print_warning "Running Handler Scaling Benchmarks (Fixed B=$FIXED_B_FOR_HANDLER_TEST)"
print_warning "============================================="

for N in "${N_VALUES[@]}"; do
    print_warning "--- Processing N = $N (Fixed B=$FIXED_B_FOR_HANDLER_TEST) ---"

    # 1. Clean
    print_info "Step 1: Cleaning..."
    if [ -d "$GENERATED_CODE_FULL_PATH" ]; then rm -rf "$GENERATED_CODE_FULL_PATH"; fi
    mkdir -p "$GENERATED_CODE_FULL_PATH"
    run_command dotnet clean "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" -v q

    # 2. Generate code for current N and FIXED B
    print_info "Step 2: Generating code (N=$N, B=$FIXED_B_FOR_HANDLER_TEST)..."
    run_command dotnet run --project "$GENERATOR_PROJECT_FILE" -- \
        -n "$N" \
        -b "$FIXED_B_FOR_HANDLER_TEST" \
        -o "$GENERATED_CODE_FULL_PATH"

    # 3. Build the benchmark project
    print_info "Step 3: Building benchmark project..."
    run_command dotnet build "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" --no-incremental

    # 4. Run BenchmarkDotNet, filtering for HandlerScalingBenchmarks class and current N
    print_info "Step 4: Running HandlerScalingBenchmarks for N=$N..."
    TARGET_FILTER="Mediator.Switch.Benchmark.HandlerScalingBenchmarks*(N: $N)" # Filter by class and N param
    ARTIFACT_PATH_N="$OUTPUT_ARTIFACTS_BASE_DIR/HandlerScaling_N${N}"
    run_command dotnet run --project "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" --no-launch-profile -- \
        --filter "$TARGET_FILTER" \
        --artifacts "$ARTIFACT_PATH_N" \
        --join

    print_success "Completed HandlerScaling for N = $N"
done

# === Run Pipeline Scaling Benchmarks ===
print_warning "============================================="
print_warning "Running Pipeline Scaling Benchmarks (Fixed N=$FIXED_N_FOR_PIPELINE_TEST)"
print_warning "============================================="

for B in "${B_VALUES[@]}"; do
    print_warning "--- Processing B = $B (Fixed N=$FIXED_N_FOR_PIPELINE_TEST) ---"

    # 1. Clean
    print_info "Step 1: Cleaning..."
    if [ -d "$GENERATED_CODE_FULL_PATH" ]; then rm -rf "$GENERATED_CODE_FULL_PATH"; fi
    mkdir -p "$GENERATED_CODE_FULL_PATH"
    run_command dotnet clean "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" -v q

    # 2. Generate code for FIXED N and current B
    print_info "Step 2: Generating code (N=$FIXED_N_FOR_PIPELINE_TEST, B=$B)..."
    run_command dotnet run --project "$GENERATOR_PROJECT_FILE" -- \
        -n "$FIXED_N_FOR_PIPELINE_TEST" \
        -b "$B" \
        -o "$GENERATED_CODE_FULL_PATH"

    # 3. Build the benchmark project
    print_info "Step 3: Building benchmark project..."
    run_command dotnet build "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" --no-incremental

    # 4. Run BenchmarkDotNet, filtering for PipelineScalingBenchmarks class and current B
    print_info "Step 4: Running PipelineScalingBenchmarks for B=$B..."
    TARGET_FILTER="Mediator.Switch.Benchmark.PipelineScalingBenchmarks*(B: $B)" # Filter by class and B param
    ARTIFACT_PATH_B="$OUTPUT_ARTIFACTS_BASE_DIR/PipelineScaling_B${B}"
    run_command dotnet run --project "$BENCHMARK_PROJECT_FILE" -c "$BUILD_CONFIG" --no-launch-profile -- \
        --filter "$TARGET_FILTER" \
        --artifacts "$ARTIFACT_PATH_B" \
        --join

    print_success "Completed PipelineScaling for B = $B"
done


print_warning "--------------------------------------------------"
print_success "Benchmark Orchestration Finished Successfully!"
print_info "Results saved in subdirectories under: $OUTPUT_ARTIFACTS_BASE_DIR"
print_warning "--------------------------------------------------"

exit 0