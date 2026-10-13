#!/usr/bin/env bash
# Usage: smoke.sh <image>. Needs a migrated PostgreSQL on localhost:5432 (database extraspace, user postgres, trust auth).
# Starts the image with both roles, so a missing time zone or a missing admin UI fails here and not on AWS.
set -euo pipefail
image="${1:?Usage: smoke.sh <image>}"
name="es-smoke-$$"
bare="es-smoke-bare-$$"
base="http://localhost:8080"
work="$(mktemp -d)"

cleanup() {
  docker rm -f "$name" "$bare" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

running() { [ "$(docker inspect -f '{{.State.Running}}' "$1" 2>/dev/null)" = "true" ]; }

# With no settings the environment is Production, which must refuse to start.
docker run -d --name "$bare" "$image" >/dev/null
for _ in $(seq 1 30); do
  running "$bare" || break
  sleep 1
done
if running "$bare" || [ "$(docker inspect -f '{{.State.ExitCode}}' "$bare" 2>/dev/null)" = "0" ]; then
  echo "Smoke check failed: the image starts without any configuration" >&2
  exit 1
fi
docker rm -f "$bare" >/dev/null 2>&1 || true

docker run -d --name "$name" --network host \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e App__Role=All \
  -e Email__Transport=Log \
  -e Seq__Url= \
  -e "ConnectionStrings__Default=Host=localhost;Port=5432;Database=extraspace;Username=postgres" \
  -e "CustomerAuth__SigningKey=$(openssl rand -base64 48)" \
  "$image" >/dev/null

for _ in $(seq 1 30); do
  if curl -fsS --max-time 5 -o /dev/null "$base/health/ready" 2>/dev/null; then ready=1; break; fi
  if ! running "$name"; then stopped=1; break; fi
  sleep 2
done
if [ "${ready:-0}" != 1 ]; then
  if [ "${stopped:-0}" = 1 ]; then echo "The container exited before it was ready." >&2
  else echo "The image did not become ready." >&2; fi
  docker logs --tail 200 "$name" >&2 || true
  exit 1
fi

fail() {
  echo "Smoke check failed: $1" >&2
  docker logs --tail 200 "$name" >&2 || true
  exit 1
}

uid="$(docker exec "$name" id -u)" || fail "the container is not running"
[[ "$uid" =~ ^[1-9][0-9]*$ ]] || fail "the container runs as root"

# Headers go to a file, never into a pipe: grep -q stops at the first match, and curl then fails on the closed pipe.
curl -sS -D "$work/page.headers" -o "$work/page.html" "$base/admin" || fail "/admin did not answer"
grep -qi '^HTTP/[0-9.]* 200' "$work/page.headers" || fail "/admin is not 200"
grep -qi '^content-type: text/html' "$work/page.headers" || fail "/admin is not HTML"
grep -qi "^content-security-policy: .*script-src 'self'" "$work/page.headers" || fail "/admin has no CSP"

asset="$(grep -o 'assets/[^"]*\.js' "$work/page.html" | head -1 || true)"
[ -n "$asset" ] || fail "index.html names no script"
curl -sS -D "$work/asset.headers" -o /dev/null -H 'Accept-Encoding: br' "$base/admin/$asset" || fail "the asset did not answer"
grep -qi '^HTTP/[0-9.]* 200' "$work/asset.headers" || fail "the asset is not served"
grep -qi '^cache-control: .*immutable' "$work/asset.headers" || fail "the asset is not immutable"
grep -qi '^content-encoding: br' "$work/asset.headers" || fail "the asset is not compressed"

curl -sS -D "$work/live.headers" -o /dev/null "$base/health/live" || fail "/health/live did not answer"
grep -qi '^HTTP/[0-9.]* 200' "$work/live.headers" || fail "/health/live is not 200"
grep -qi '^x-trace-id:' "$work/live.headers" || fail "no X-Trace-Id"
echo "Smoke checks passed."
