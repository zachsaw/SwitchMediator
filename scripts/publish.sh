#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e
# Treat unset variables as an error when substituting.
set -u
# Prevent errors in a pipeline from being masked.
set -o pipefail

# --- Configuration ---

# Get the version number from the first script argument
if [ -z "$1" ]; then
  echo "Usage: $0 <VERSION>"
  echo "Example: $0 1.0.0"
  exit 1
fi
VERSION="$1"

# Check if NUGET_API_KEY is set
if [ -z "${NUGET_API_KEY}" ]; then
  echo "Error: NUGET_API_KEY environment variable is not set."
  echo "Please set it before running the script:"
  echo "export NUGET_API_KEY=your_actual_api_key"
  exit 1
fi

# Define project paths relative to the script's directory's parent (repo root)
SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
REPO_ROOT=$(realpath "$SCRIPT_DIR/..") # Assumes script is in 'scripts' folder one level down from root

CORE_PROJ_NAME="Mediator.Switch"
CORE_PROJ_PATH="$REPO_ROOT/src/$CORE_PROJ_NAME/$CORE_PROJ_NAME.csproj"

EXT_PROJ_NAME="Mediator.Switch.Extensions.Microsoft.DependencyInjection"
EXT_PROJ_PATH="$REPO_ROOT/src/$EXT_PROJ_NAME/$EXT_PROJ_NAME.csproj"

ARTIFACTS_DIR="$REPO_ROOT/artifacts" # Directory to store the created .nupkg files
NUGET_SOURCE="https://api.nuget.org/v3/index.json"

# Validate paths
if [ ! -f "$CORE_PROJ_PATH" ]; then
    echo "Error: Core project file not found at $CORE_PROJ_PATH"
    exit 1
fi
if [ ! -f "$EXT_PROJ_PATH" ]; then
    echo "Error: Extensions project file not found at $EXT_PROJ_PATH"
    exit 1
fi

echo "--------------------------------------------------"
echo " Starting NuGet Publish Process"
echo " Version:        $VERSION"
echo " Core Project:   $CORE_PROJ_PATH"
echo " Ext Project:    $EXT_PROJ_PATH"
echo " Artifacts Dir:  $ARTIFACTS_DIR"
echo "--------------------------------------------------"

# --- Clean ---
echo Cleaning previous artifacts...
rm -rf "$ARTIFACTS_DIR"
mkdir -p "$ARTIFACTS_DIR"

# Clean solution/projects (optional but recommended)
# dotnet clean "$REPO_ROOT/SwitchMediator.sln" -c Release # If you have a solution file
dotnet clean "$CORE_PROJ_PATH" -c Release
dotnet clean "$EXT_PROJ_PATH" -c Release

# --- Build & Pack ---
# Note: `dotnet pack` automatically builds in Release config by default if -c is specified.
# We pass the version via /p:PackageVersion to override what's in the csproj for this specific pack operation.

echo "Packing $CORE_PROJ_NAME version $VERSION..."
dotnet pack "$CORE_PROJ_PATH" \
  -c Release \
  /p:PackageVersion="$VERSION" \
  --output "$ARTIFACTS_DIR"

echo "Packing $EXT_PROJ_NAME version $VERSION..."
dotnet pack "$EXT_PROJ_PATH" \
  -c Release \
  /p:PackageVersion="$VERSION" \
  --output "$ARTIFACTS_DIR"

echo "--------------------------------------------------"
echo " Created Packages:"
ls -l "$ARTIFACTS_DIR"
echo "--------------------------------------------------"


# --- Push ---
echo "Pushing packages to NuGet ($NUGET_SOURCE)..."

# Push all .nupkg files found in the artifacts directory
# --skip-duplicate: Don't fail if the package version already exists (useful for retries, but be cautious)
dotnet nuget push "$ARTIFACTS_DIR/*.nupkg" \
  --api-key "$NUGET_API_KEY" \
  --source "$NUGET_SOURCE" \
  --skip-duplicate

echo "--------------------------------------------------"
echo " Successfully published version $VERSION to NuGet!"
echo "--------------------------------------------------"

exit 0