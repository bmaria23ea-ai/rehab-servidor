#!/usr/bin/env bash
# Copia cifrada de la base: mariadb-dump | gzip | age (solo llave pública). Retención: RESPALDO_DIAS.
# Uso: ./scripts/respaldo.sh   (destinos en .env: RESPALDO_LOCAL, RESPALDO_DESTINO)
set -euo pipefail
umask 077
cd "$(dirname "$0")/.."

leer_env() { [ -f .env ] && sed -n "s/^$1=//p" .env | tail -n1 || true; }
LOCAL="$(leer_env RESPALDO_LOCAL)";   LOCAL="${LOCAL:-$HOME/respaldos-rehab}"
DESTINO="$(leer_env RESPALDO_DESTINO)"
DIAS="$(leer_env RESPALDO_DIAS)";     DIAS="${DIAS:-30}"
PUBLICA=secrets/respaldo_publica.txt

[ -s "$PUBLICA" ] || { echo "Falta $PUBLICA" >&2; exit 1; }
mkdir -p "$LOCAL"
nombre="rehab_$(date +%Y%m%d_%H%M%S).sql.gz.age"
tmp="$LOCAL/.$nombre.tmp"
trap 'rm -f "$tmp"' EXIT

docker compose exec -T db sh -c 'MYSQL_PWD="$(cat /run/secrets/db_root_password)" exec mariadb-dump -uroot \
    --single-transaction --routines --triggers --events --hex-blob --databases rehab' \
  | gzip -9 | age -R "$PUBLICA" > "$tmp"

head -c 21 "$tmp" | grep -q '^age-encryption.org/v1' || { echo "Copia no válida" >&2; exit 1; }
mv "$tmp" "$LOCAL/$nombre"
echo "Copia: $LOCAL/$nombre ($(du -h "$LOCAL/$nombre" | cut -f1))"

if [ -n "$DESTINO" ]; then
    mkdir -p "$DESTINO"
    cp "$LOCAL/$nombre" "$DESTINO/"
    echo "Copia externa: $DESTINO/$nombre"
fi

for dir in "$LOCAL" ${DESTINO:+"$DESTINO"}; do
    find "$dir" -maxdepth 1 -name 'rehab_*.sql.gz.age' -mtime +"$DIAS" -print -delete
done
