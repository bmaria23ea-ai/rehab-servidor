-- Gimnasio: una sesión = un ejercicio; la cabecera sigue en `sesiones`.
-- Sin duración configurada (termina al completar las series); la marcha sigue enviándola.
ALTER TABLE sesiones MODIFY COLUMN duracion_configurada_s SMALLINT UNSIGNED NULL;

CREATE TABLE gimnasio_sesiones (
    sesion_id      BIGINT UNSIGNED   NOT NULL,
    series_config  TINYINT UNSIGNED  NOT NULL,
    meta_por_serie TINYINT UNSIGNED  NOT NULL,
    umbral_logro   DECIMAL(3,2)      NOT NULL,
    retiros_visor  SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (sesion_id),
    CONSTRAINT fk_gimnasio_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE,
    CONSTRAINT ck_gimnasio_config CHECK (series_config BETWEEN 1 AND 10 AND meta_por_serie BETWEEN 1 AND 40
        AND umbral_logro BETWEEN 0 AND 1 AND retiros_visor <= 1000)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE gimnasio_series (
    sesion_id    BIGINT UNSIGNED  NOT NULL,
    serie        TINYINT UNSIGNED NOT NULL,
    reps         TINYINT UNSIGNED NOT NULL,
    reps_validas TINYINT UNSIGNED NOT NULL,
    PRIMARY KEY (sesion_id, serie),
    CONSTRAINT fk_gimnasio_serie FOREIGN KEY (sesion_id) REFERENCES gimnasio_sesiones (sesion_id) ON DELETE CASCADE,
    CONSTRAINT ck_gimnasio_serie CHECK (serie BETWEEN 1 AND 10 AND reps <= 100 AND reps_validas <= reps)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (13, 'Sesiones del gimnasio');
