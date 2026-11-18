-- Migración 17: ficha de retroalimentación del entorno de RV (una por paciente, la llena el terapeuta).
-- La exposición se guarda fija al llenar: suma de las sesiones del primer día del paciente.
CREATE TABLE fichas_retroalimentacion (
    paciente_id         INT UNSIGNED      NOT NULL,
    exposicion_s        DECIMAL(7,1)      NOT NULL,
    exposicion_dia      DATE              NOT NULL,
    exposicion_sesiones SMALLINT UNSIGNED NOT NULL,
    respuesta           ENUM('normal','mareo','desorientacion','otro') NOT NULL,
    respuesta_otro      VARCHAR(60)       NULL,
    tolerancia          ENUM('buena','regular','mala') NOT NULL,
    calificaciones      CHAR(16)          NOT NULL,   -- 4 dimensiones x A-D, cada una de 1 a 5
    terapeuta           VARCHAR(30)       NOT NULL,
    actualizado_utc     DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    PRIMARY KEY (paciente_id),
    CONSTRAINT fk_ficha_paciente FOREIGN KEY (paciente_id) REFERENCES pacientes (id) ON DELETE CASCADE,
    CONSTRAINT ck_ficha_exposicion CHECK (exposicion_s BETWEEN 0 AND 86400 AND exposicion_sesiones >= 1),
    CONSTRAINT ck_ficha_otro CHECK ((respuesta = 'otro') = (respuesta_otro IS NOT NULL)),
    CONSTRAINT ck_ficha_notas CHECK (calificaciones REGEXP '^[1-5]{16}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (17, 'Ficha de retroalimentación del entorno de RV');
