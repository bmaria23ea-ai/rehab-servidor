#!/bin/sh
# Certbot --deploy-hook: copia el certificado renovado al volumen que lee Nginx (usuario 101).
set -eu
cp -L "$RENEWED_LINEAGE/fullchain.pem" "$RENEWED_LINEAGE/privkey.pem" /salida/
chown 101:101 /salida/fullchain.pem /salida/privkey.pem
chmod 400 /salida/fullchain.pem /salida/privkey.pem
