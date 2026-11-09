#!/bin/sh
# Runs nats-server (config written by the API) and nats-api, and reloads nats-server when the API rewrites the config.
set -eu

: "${TACTICAL_SECRET:?TACTICAL_SECRET is required}"
: "${DB_HOST:=db}" "${DB_PORT:=5432}" "${DB_NAME:=tacticalrmm}" "${DB_USER:=tactical}" "${DB_PASSWORD:?DB_PASSWORD is required}" "${DB_SSLMODE:=disable}"
: "${NATS_CONF:=/data/nats/nats-rmm.conf}" "${NATS_URL:=nats://127.0.0.1:4222}" "${CHECK_INTERVAL:=2}"
API_CONF=/tmp/nats-api.conf

# Minimal JSON string escaping (backslash and double quote).
esc() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }

# nats-server cannot start without a config file; the API replaces this one with the full agent config on the first agent registration.
if [ ! -f "$NATS_CONF" ]; then
  printf '{"accounts":{"TRMM":{"users":[{"user":"tacticalrmm","password":"%s","permissions":{"publish":">","subscribe":">"}}],"mappings":{"*":"cmd.{{wildcard(1)}}"},"exports":[{"stream":"cmd.*"}],"imports":[{"stream":{"account":"AGENTS","subject":"fromagent.*"},"to":"{{wildcard(1)}}"},{"stream":{"account":"AGENTS","subject":"_INBOX.>"}},{"stream":{"account":"AGENTS","subject":"*.cmdoutput.>"}},{"stream":{"account":"AGENTS","subject":"*.terminal.>"}}]},"AGENTS":{"users":[],"mappings":{"*":"fromagent.{{wildcard(1)}}"},"exports":[{"stream":"fromagent.*"},{"stream":"_INBOX.>"},{"stream":"*.cmdoutput.>"},{"stream":"*.terminal.>"}],"imports":[{"stream":{"account":"TRMM","subject":"cmd.*"},"to":"{{wildcard(1)}}"}]}},"max_payload":67108864,"host":"0.0.0.0","port":4222,"websocket":{"host":"127.0.0.1","port":9235,"no_tls":true}}' "$(esc "$TACTICAL_SECRET")" > "$NATS_CONF"
  echo "wrote initial $NATS_CONF"
fi

printf '{"key":"%s","natsurl":"%s","user":"%s","pass":"%s","host":"%s","port":%s,"dbname":"%s","sslmode":"%s"}' \
  "$(esc "$TACTICAL_SECRET")" "$(esc "$NATS_URL")" "$(esc "$DB_USER")" "$(esc "$DB_PASSWORD")" "$(esc "$DB_HOST")" "$DB_PORT" "$(esc "$DB_NAME")" "$(esc "$DB_SSLMODE")" > "$API_CONF"
chmod 600 "$API_CONF"

nats-server --config "$NATS_CONF" &
NATS_PID=$!

# Reload when the config file changes (the API rewrites it whenever an agent is added or removed).
(
  last=$(stat -c %Y "$NATS_CONF")
  while sleep "$CHECK_INTERVAL"; do
    now=$(stat -c %Y "$NATS_CONF" 2>/dev/null || echo "$last")
    if [ "$now" != "$last" ]; then last=$now; echo "config changed, reloading nats-server"; kill -HUP "$NATS_PID" 2>/dev/null || true; fi
  done
) &
WATCH_PID=$!

# nats-api needs both NATS and the database; restart it if it exits (e.g. the database is still starting).
(
  sleep 2
  while true; do /usr/local/bin/nats-api -config "$API_CONF" || true; echo "nats-api exited, restarting in 5s"; sleep 5; done
) &
NATSAPI_PID=$!

trap 'kill $NATS_PID $WATCH_PID $NATSAPI_PID 2>/dev/null || true' TERM INT
wait "$NATS_PID"
kill "$WATCH_PID" "$NATSAPI_PID" 2>/dev/null || true
