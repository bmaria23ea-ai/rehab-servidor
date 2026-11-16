-- Respiración: una sesión = un ejercicio (apnea en sedestación); la cabecera sigue en `sesiones`.
-- La duración real la mide el visor con un reloj monótono (sesiones.duracion_real_s).
CREATE TABLE respiracion_sesiones (
    sesion_id      BIGINT UNSIGNED   NOT NULL,
    series_config  TINYINT UNSIGNED  NOT NULL,
    reps_por_serie TINYINT UNSIGNED  NOT NULL,
    retiros_visor  SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (sesion_id),
    CONSTRAINT fk_respiracion_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE,
    CONSTRAINT ck_respiracion_config CHECK (series_config BETWEEN 1 AND 10 AND reps_por_serie BETWEEN 1 AND 20
        AND retiros_visor <= 1000)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE respiracion_series (
    sesion_id BIGINT UNSIGNED  NOT NULL,
    serie     TINYINT UNSIGNED NOT NULL,
    reps      TINYINT UNSIGNED NOT NULL,
    PRIMARY KEY (sesion_id, serie),
    CONSTRAINT fk_respiracion_serie FOREIGN KEY (sesion_id) REFERENCES respiracion_sesiones (sesion_id) ON DELETE CASCADE,
    CONSTRAINT ck_respiracion_serie CHECK (serie BETWEEN 1 AND 10 AND reps <= 20)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (15, 'Sesiones de respiración');
