-- Mínimo privilegio: la API solo puede leer y escribir filas, no cambiar ni borrar tablas.
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'rehab_api'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON rehab.* TO 'rehab_api'@'%';
FLUSH PRIVILEGES;
