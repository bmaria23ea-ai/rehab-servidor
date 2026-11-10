#!/usr/bin/env bash
# Prueba de restauración: descifra una copia en un MariaDB temporal (en RAM, sin red) y compara filas con la base actual.
# Uso: ./scripts/probar_restauracion.sh <llave_privada> [copia]   (por defecto, la copia local más reciente)
set -euo pipefail
cd "$(dirname "$0")/.."

LLAVE="${1:?Uso: $0 <llave_privada> [copia]}"
COPIA="${2:-$(ls -t "$HOME"/respaldos-rehab/rehab_*.sql.gz.age 2>/dev/null | head -n1)}"
[ -f "$COPIA" ] || { echo "No hay copia para probar" >&2; exit 1; }
IMAGEN=mariadb:12.3
NOMBRE=rehab-prueba-restauracion
CLAVE="$(openssl rand -hex 24)"

docker rm -f "$NOMBRE" >/dev/null 2>&1 || true
trap 'docker rm -f "$NOMBRE" >/dev/null 2>&1 || true' EXIT
docker run -d --name "$NOMBRE" --network none --tmpfs /var/lib/mysql:size=512m \
    -e MARIADB_ROOT_PASSWORD="$CLAVE" "$IMAGEN" >/dev/null

prueba() { docker exec -i -e MYSQL_PWD="$CLAVE" "$NOMBRE" mariadb -uroot -N rehab; }
actual() { docker compose exec -T db sh -c 'MYSQL_PWD="$(cat /run/secrets/db_root_password)" exec mariadb -uroot -N rehab'; }

for _ in $(seq 60); do
    docker logs "$NOMBRE" 2>&1 | grep -q "init process done" \
        && docker exec -e MYSQL_PWD="$CLAVE" "$NOMBRE" mariadb -uroot -e "SELECT 1" >/dev/null 2>&1 && break
    sleep 2
done

echo "Restaurando $(basename "$COPIA")..."
age -d -i "$LLAVE" "$COPIA" | gunzip -c | docker exec -i -e MYSQL_PWD="$CLAVE" "$NOMBRE" mariadb -uroot

fallos=0
printf "%-28s %8s %8s\n" "Tabla" "Actual" "Copia"
for t in $(echo "SELECT table_name FROM information_schema.tables WHERE table_schema='rehab' AND table_type='BASE TABLE' ORDER BY 1" | prueba); do
    a="$(echo "SELECT COUNT(*) FROM \`$t\`" | actual)"
    b="$(echo "SELECT COUNT(*) FROM \`$t\`" | prueba)"
    estado=OK; [ "$a" = "$b" ] || { estado=DIFERENTE; fallos=$((fallos + 1)); }
    printf "%-28s %8s %8s  %s\n" "$t" "$a" "$b" "$estado"
done
echo "Triggers en la copia: $(echo "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema='rehab'" | prueba)"
[ "$fallos" -eq 0 ] && echo "Restauración correcta." || echo "$fallos tablas difieren (normal si hubo actividad después de la copia)."
