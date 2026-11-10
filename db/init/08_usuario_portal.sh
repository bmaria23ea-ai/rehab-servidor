#!/bin/sh
# Migración 8: usuario de MariaDB del portal, con permisos mínimos.
# Se ejecuta sola en una instalación nueva; en una base existente:
#   docker compose exec db sh /docker-entrypoint-initdb.d/08_usuario_portal.sh
set -eu

PORTAL_PASS="$(cat /run/secrets/db_portal_password)"
export MYSQL_PWD="$(cat /run/secrets/db_root_password)"

mariadb --protocol=socket -uroot rehab <<SQL
CREATE USER IF NOT EXISTS 'rehab_portal'@'%' IDENTIFIED BY '${PORTAL_PASS}';
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'rehab_portal'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON rehab.* TO 'rehab_portal'@'%';
INSERT IGNORE INTO esquema_migraciones (version, descripcion) VALUES (8, 'Usuario de MariaDB del portal');
FLUSH PRIVILEGES;
SQL
echo "Usuario rehab_portal listo."
