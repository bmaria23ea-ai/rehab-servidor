-- Migración 6: duración también en minutos (fase 3).
-- Columnas calculadas (VIRTUAL): no ocupan espacio ni se escriben; se calculan al consultar
-- a partir de las columnas en segundos, que siguen siendo el dato original.
-- En una base existente se aplica una vez, a mano, con el usuario root.

ALTER TABLE sesiones
    ADD COLUMN duracion_configurada_min DECIMAL(6,2)
        AS (ROUND(duracion_configurada_s / 60, 2)) VIRTUAL AFTER duracion_configurada_s,
    ADD COLUMN duracion_real_min DECIMAL(6,2)
        AS (ROUND(duracion_real_s / 60, 2)) VIRTUAL AFTER duracion_real_s;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (6, 'Duración en minutos (columnas calculadas)');
