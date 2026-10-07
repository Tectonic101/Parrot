#!/usr/bin/env python3
"""Small App Store Connect API helper for the TestFlight workflow.

  asc.py ensure-bundle-id <bundle id> <name>   register the id if it's new
  asc.py app-exists <bundle id>                exit 0 if an app record uses it

Reads ASC_KEY_ID, ASC_ISSUER_ID and ASC_KEY_P8 from the environment.
"""
import json, os, sys, time, urllib.error, urllib.parse, urllib.request

import jwt  # PyJWT[crypto]

API = "https://api.appstoreconnect.apple.com/v1"


def token():
    now = int(time.time())
    return jwt.encode(
        {"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 900, "aud": "appstoreconnect-v1"},
        os.environ["ASC_KEY_P8"],
        algorithm="ES256",
        headers={"kid": os.environ["ASC_KEY_ID"], "typ": "JWT"},
    )


def call(method, path, body=None):
    req = urllib.request.Request(
        API + path,
        method=method,
        data=json.dumps(body).encode() if body else None,
        headers={"Authorization": "Bearer " + token(), "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"App Store Connect {method} {path} failed: {e.code} {e.read().decode()[:500]}")


def main():
    cmd, ident = sys.argv[1], sys.argv[2]
    q = urllib.parse.quote(ident)
    if cmd == "ensure-bundle-id":
        found = call("GET", f"/bundleIds?filter[identifier]={q}")["data"]
        if any(b["attributes"]["identifier"] == ident for b in found):
            print(f"Bundle id {ident} already registered.")
            return
        call("POST", "/bundleIds", {"data": {"type": "bundleIds", "attributes": {
            "identifier": ident, "name": sys.argv[3], "platform": "IOS"}}})
        print(f"Registered bundle id {ident}.")
    elif cmd == "app-exists":
        apps = call("GET", f"/apps?filter[bundleId]={q}")["data"]
        sys.exit(0 if any(a["attributes"]["bundleId"] == ident for a in apps) else 1)
    else:
        sys.exit(f"unknown command {cmd}")


if __name__ == "__main__":
    main()
