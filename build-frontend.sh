#!/bin/bash
# Build script for FleetManager Frontend

set -e

echo "Building FleetManager Frontend..."
echo "================================="

cd "$(dirname "$0")/Vda5050FleetController/wwwroot"

echo "Installing npm dependencies..."
npm install

echo "Building TypeScript and bundling..."
npm run build

echo ""
echo "✓ Frontend build completed successfully!"
echo "  Output: Vda5050FleetController/wwwroot/dist/bundle.js"
