-- Migración 12: mediciones que registra el terapeuta en la marcha (oxímetro y tensiómetro).
-- Borg: escala modificada 0-10 (ATS 2002). PA solo en reposo y final.
CREATE TABLE marcha_mediciones (
    sesion_id       BIGINT UNSIGNED  NOT NULL,
    momento         ENUM('reposo','min1','min3','min5','final') NOT NULL,
    fc              SMALLINT UNSIGNED NULL,
    spo2            TINYINT UNSIGNED  NULL,
    borg            DECIMAL(3,1)      NULL,
    pa_sistolica    SMALLINT UNSIGNED NULL,
    pa_diastolica   SMALLINT UNSIGNED NULL,
    terapeuta       VARCHAR(30)       NOT NULL,
    actualizado_utc DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    PRIMARY KEY (sesion_id, momento),
    CONSTRAINT fk_medicion_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE,
    CONSTRAINT ck_medicion_fc   CHECK (fc BETWEEN 20 AND 250),
    CONSTRAINT ck_medicion_spo2 CHECK (spo2 <= 100),
    CONSTRAINT ck_medicion_borg CHECK (borg BETWEEN 0 AND 10 AND borg * 2 = FLOOR(borg * 2)),
    CONSTRAINT ck_medicion_pa   CHECK ((pa_sistolica IS NULL) = (pa_diastolica IS NULL)
                                       AND (pa_sistolica IS NULL OR (momento IN ('reposo','final')
                                       AND pa_sistolica <= 300 AND pa_sistolica > pa_diastolica)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO esquema_migraciones (version, descripcion) VALUES (12, 'Mediciones del terapeuta en la marcha');
