@echo off
setlocal enabledelayedexpansion

:: --- Configuration ---
set "BENCHMARK_PROJECT_DIR=.\Mediator.Switch.Benchmark"
set "GENERATOR_PROJECT_DIR=.\Mediator.Switch.Benchmark.Generator"
set "GENERATED_CODE_DIR_NAME=Generated"
set "OUTPUT_ARTIFACTS_BASE_DIR=.\BenchmarkDotNet.Artifacts"

:: Define N and B values to test (space-separated)
set "N_VALUES=25 100 600"
set "B_VALUES=1 5"

:: Fixed values for cross-benchmarking
set "FIXED_N_FOR_PIPELINE_TEST=100"
set "FIXED_B_FOR_HANDLER_TEST=0"

set "BENCHMARK_PROJECT_FILE=%BENCHMARK_PROJECT_DIR%\Mediator.Switch.Benchmark.csproj"
set "GENERATOR_PROJECT_FILE=%GENERATOR_PROJECT_DIR%\Mediator.Switch.Benchmark.Generator.csproj"
set "GENERATED_CODE_FULL_PATH=%BENCHMARK_PROJECT_DIR%\%GENERATED_CODE_DIR_NAME%"
set "BUILD_CONFIG=Release"
:: --- End Configuration ---

echo Starting Benchmark Orchestration...
if not exist "%OUTPUT_ARTIFACTS_BASE_DIR%\" mkdir "%OUTPUT_ARTIFACTS_BASE_DIR%"

:: === Run Handler Scaling Benchmarks ===
echo =============================================
echo Running Handler Scaling Benchmarks (Fixed B=%FIXED_B_FOR_HANDLER_TEST%)
echo =============================================

for %%N in (%N_VALUES%) do (
    call :ProcessHandlerScaling %%N %FIXED_B_FOR_HANDLER_TEST%
    if errorlevel 1 goto :error_exit
)

:: === Run Pipeline Scaling Benchmarks ===
echo =============================================
echo Running Pipeline Scaling Benchmarks (Fixed N=%FIXED_N_FOR_PIPELINE_TEST%)
echo =============================================

for %%B in (%B_VALUES%) do (
    call :ProcessPipelineScaling %FIXED_N_FOR_PIPELINE_TEST% %%B
    if errorlevel 1 goto :error_exit
)

echo --------------------------------------------------
echo SUCCESS: Benchmark Orchestration Finished Successfully!
echo Results saved in subdirectories under: %OUTPUT_ARTIFACTS_BASE_DIR%
echo --------------------------------------------------

goto :eof

:: --- Subroutines ---

:ProcessHandlerScaling
:: %1 = N, %2 = B
set "CURRENT_N=%1"
set "CURRENT_B=%2"
echo --- Processing N = %CURRENT_N% ^(Fixed B=%CURRENT_B%^) ---

:: 1. Clean
echo Step 1: Cleaning...
if exist "%GENERATED_CODE_FULL_PATH%\" (
    echo Removing existing generated code directory: !GENERATED_CODE_FULL_PATH!
    rd /s /q "%GENERATED_CODE_FULL_PATH%"
    if errorlevel 1 ( echo ERROR: Failed to remove directory "%GENERATED_CODE_FULL_PATH%". & exit /b 1 )
)
if not exist "%GENERATED_CODE_FULL_PATH%\" (
    mkdir "%GENERATED_CODE_FULL_PATH%"
    if errorlevel 1 ( echo ERROR: Failed to create directory "%GENERATED_CODE_FULL_PATH%". & exit /b 1 )
)
call :run_command dotnet clean "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" -v q
if errorlevel 1 exit /b 1

:: 2. Generate code for current N and B
echo Step 2: Generating code (N=%CURRENT_N%, B=%CURRENT_B%)...
call :run_command dotnet run --project "%GENERATOR_PROJECT_FILE%" -- -n %CURRENT_N% -b %CURRENT_B% -o "%GENERATED_CODE_FULL_PATH%"
if errorlevel 1 exit /b 1

:: 3. Build the benchmark project
echo Step 3: Building benchmark project...
call :run_command dotnet build "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-incremental
if errorlevel 1 exit /b 1

:: 4. Run BenchmarkDotNet, filtering for HandlerScalingBenchmarks class and current N
echo Step 4: Running HandlerScalingBenchmarks for N=%CURRENT_N%...
set "TARGET_FILTER=Mediator.Switch.Benchmark.HandlerScalingBenchmarks*(N: %CURRENT_N%)"
set "ARTIFACT_PATH_N=!OUTPUT_ARTIFACTS_BASE_DIR!\HandlerScaling_N%CURRENT_N%"
:: Pass filter and artifacts using delayed expansion to handle special chars safely within the variable value
call :run_command dotnet run --project "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-launch-profile -- --filter "!TARGET_FILTER!" --artifacts "!ARTIFACT_PATH_N!" --join
if errorlevel 1 exit /b 1

echo SUCCESS: Completed HandlerScaling for N = %CURRENT_N%
exit /b 0


:ProcessPipelineScaling
:: %1 = N, %2 = B
set "CURRENT_N=%1"
set "CURRENT_B=%2"
echo --- Processing B = %CURRENT_B% ^(Fixed N=%CURRENT_N%^) ---

:: 1. Clean
echo Step 1: Cleaning...
if exist "%GENERATED_CODE_FULL_PATH%\" (
    echo Removing existing generated code directory: !GENERATED_CODE_FULL_PATH!
    rd /s /q "%GENERATED_CODE_FULL_PATH%"
    if errorlevel 1 ( echo ERROR: Failed to remove directory "%GENERATED_CODE_FULL_PATH%". & exit /b 1 )
)
if not exist "%GENERATED_CODE_FULL_PATH%\" (
    mkdir "%GENERATED_CODE_FULL_PATH%"
     if errorlevel 1 ( echo ERROR: Failed to create directory "%GENERATED_CODE_FULL_PATH%". & exit /b 1 )
)
call :run_command dotnet clean "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" -v q
if errorlevel 1 exit /b 1

:: 2. Generate code for current N and B
echo Step 2: Generating code (N=%CURRENT_N%, B=%CURRENT_B%)...
call :run_command dotnet run --project "%GENERATOR_PROJECT_FILE%" -- -n %CURRENT_N% -b %CURRENT_B% -o "%GENERATED_CODE_FULL_PATH%"
if errorlevel 1 exit /b 1

:: 3. Build the benchmark project
echo Step 3: Building benchmark project...
call :run_command dotnet build "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-incremental
if errorlevel 1 exit /b 1

:: 4. Run BenchmarkDotNet, filtering for PipelineScalingBenchmarks class and current B
echo Step 4: Running PipelineScalingBenchmarks for B=%CURRENT_B%...
set "TARGET_FILTER=Mediator.Switch.Benchmark.PipelineScalingBenchmarks*(B: %CURRENT_B%)"
set "ARTIFACT_PATH_B=!OUTPUT_ARTIFACTS_BASE_DIR!\PipelineScaling_B%CURRENT_B%"
:: Pass filter and artifacts using delayed expansion to handle special chars safely within the variable value
call :run_command dotnet run --project "%BENCHMARK_PROJECT_FILE%" -c "%BUILD_CONFIG%" --no-launch-profile -- --filter "!TARGET_FILTER!" --artifacts "!ARTIFACT_PATH_B!" --join
if errorlevel 1 exit /b 1

echo SUCCESS: Completed PipelineScaling for B = %CURRENT_B%
exit /b 0


:run_command
echo Executing: %*
%*
if %ERRORLEVEL% neq 0 (
    echo ERROR: Command failed with error code %ERRORLEVEL%: %*
    :: Exit the subroutine with the error code
    exit /b %ERRORLEVEL%
)
echo SUCCESS: Command executed successfully.
exit /b 0


:error_exit
echo ERROR: A critical error occurred. Exiting script.
exit /b 1

:: --- End Subroutines ---