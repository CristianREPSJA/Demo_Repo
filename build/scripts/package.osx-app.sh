#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

mkdir -p Rama.app/Contents/Resources
mv SourceGit Rama.app/Contents/MacOS
cp resources/app/App.icns Rama.app/Contents/Resources/App.icns
sed "s/SOURCE_GIT_VERSION/$VERSION/g" resources/app/App.plist > Rama.app/Contents/Info.plist
rm -rf Rama.app/Contents/MacOS/SourceGit.dsym
rm -f Rama.app/Contents/MacOS/*.pdb

case "$RUNTIME" in
  osx-arm64) ARCH=arm64 ;;
  osx-x64)   ARCH=x86_64 ;;
  *) echo "Unsupported RUNTIME: $RUNTIME" >&2; exit 1 ;;
esac

clang -arch "$ARCH" ../tools/setsid-macos/setsid.c -o Rama.app/Contents/MacOS/setsid -mmacosx-version-min=13.0
codesign --force --deep --sign - Rama.app
codesign --verify --deep --strict --verbose=2 Rama.app

zip "rama_$VERSION.$RUNTIME.zip" -r Rama.app
