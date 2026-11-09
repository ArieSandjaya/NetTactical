# NATS server + the original Tactical RMM `nats-api` (Go), in one small image.
# nats-api is the unchanged upstream binary (Tactical RMM License: internal use); it is downloaded at build time, not stored in this repo.
FROM alpine:3.20
ARG NATS_VERSION=2.10.22
ARG TRMM_REF=v1.6.1
ARG TARGETARCH
RUN apk add --no-cache ca-certificates curl tar \
 && case "${TARGETARCH:-amd64}" in arm64) NA=nats-api-arm64 ;; *) NA=nats-api ;; esac \
 && curl -fsSL "https://github.com/nats-io/nats-server/releases/download/v${NATS_VERSION}/nats-server-v${NATS_VERSION}-linux-${TARGETARCH:-amd64}.tar.gz" \
      | tar -xz --strip-components=1 -C /usr/local/bin "nats-server-v${NATS_VERSION}-linux-${TARGETARCH:-amd64}/nats-server" \
 && curl -fsSL -o /usr/local/bin/nats-api "https://raw.githubusercontent.com/amidaware/tacticalrmm/${TRMM_REF}/natsapi/bin/${NA}" \
 && chmod +x /usr/local/bin/nats-server /usr/local/bin/nats-api \
 && apk del curl tar
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
EXPOSE 4222
ENTRYPOINT ["/entrypoint.sh"]
