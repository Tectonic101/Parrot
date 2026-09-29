#!/bin/sh
# Starts Parrot's read-only meeting server for Claude. No network, no writes.
for app in '/Applications/Parrot.app' "$HOME/Applications/Parrot.app" \
    "$(mdfind "kMDItemCFBundleIdentifier == 'com.uygar.parrot'" 2>/dev/null | head -n 1)"; do
  if [ -x "$app/Contents/MacOS/Parrot" ]; then exec "$app/Contents/MacOS/Parrot" --mcp; fi
done
echo "Parrot isn't installed. Get it at https://openparrot.app" >&2
exit 1
