-- Esquema v1: sesiones de rehabilitación (por ahora solo la caminata de 6 minutos).
-- Se ejecuta una sola vez, cuando el contenedor de MariaDB crea la base vacía.
-- Todas las fechas se guardan en UTC.

SET NAMES utf8mb4;

-- Pacientes: solo un código seudónimo (P-001), nunca nombres ni otros datos personales.
CREATE TABLE pacientes (
    id                     INT UNSIGNED  NOT NULL AUTO_INCREMENT,
    codigo                 VARCHAR(10)   NOT NULL,
    es_prueba              BOOLEAN       NOT NULL DEFAULT FALSE,   -- P-000 y otros pacientes ficticios
    consentimiento_utc     DATETIME(3)   NULL,                     -- cuándo firmó el consentimiento (LOPDP)
    consentimiento_version VARCHAR(20)   NULL,                     -- versión del formulario firmado
    retencion_hasta        DATE          NULL,                     -- plazo de conservación (lo define el equipo clínico)
    creado_utc             DATETIME(3)   NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    PRIMARY KEY (id),
    UNIQUE KEY uq_pacientes_codigo (codigo),
    CONSTRAINT ck_pacientes_codigo CHECK (codigo REGEXP '^P-[0-9]{3,6}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Visores Quest registrados. Cada uno se puede revocar si se pierde.
-- Los campos de autenticación se agregan en la fase 2.
CREATE TABLE visores (
    id             SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
    nombre         VARCHAR(40)       NOT NULL,                     -- ej. "Quest 3 - 01"
    estado         ENUM('activo','revocado') NOT NULL DEFAULT 'activo',
    registrado_utc DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)),
    revocado_utc   DATETIME(3)       NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_visores_nombre (nombre)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Datos comunes a cualquier ejercicio (caminata, gimnasio, respiración).
CREATE TABLE sesiones (
    id                     BIGINT UNSIGNED   NOT NULL AUTO_INCREMENT,
    sesion_uuid            UUID              NOT NULL,             -- generado en el visor: evita duplicados al reenviar
    paciente_id            INT UNSIGNED      NOT NULL,
    visor_id               SMALLINT UNSIGNED NOT NULL,
    ejercicio              VARCHAR(32)       NOT NULL,             -- 'caminata_6m' (luego 'sentadilla_libre', etc.)
    escena                 VARCHAR(64)       NOT NULL,             -- nombre de la escena de Unity
    version_esquema        SMALLINT UNSIGNED NOT NULL,             -- versión del formato enviado por el visor
    version_app            VARCHAR(20)       NOT NULL,
    es_prueba              BOOLEAN           NOT NULL DEFAULT FALSE,
    inicio_utc             DATETIME(3)       NOT NULL,             -- reloj del visor
    fin_utc                DATETIME(3)       NOT NULL,             -- reloj del visor
    recibido_utc           DATETIME(3)       NOT NULL DEFAULT (UTC_TIMESTAMP(3)), -- reloj del servidor
    duracion_configurada_s SMALLINT UNSIGNED NOT NULL,             -- 120 en pruebas, 360 en la caminata real
    duracion_real_s        DECIMAL(6,1)      NOT NULL,
    estado                 ENUM('completa','incompleta') NOT NULL,
    motivo_fin             ENUM('completada','detenida','salida_menu','visor_retirado','app_pausada','error') NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_sesiones_uuid (sesion_uuid),
    KEY ix_sesiones_paciente_inicio (paciente_id, inicio_utc),     -- evolución de un paciente
    KEY ix_sesiones_visor (visor_id),
    CONSTRAINT fk_sesiones_paciente FOREIGN KEY (paciente_id) REFERENCES pacientes (id) ON DELETE CASCADE,
    CONSTRAINT fk_sesiones_visor    FOREIGN KEY (visor_id)    REFERENCES visores (id),
    CONSTRAINT ck_sesiones_fechas   CHECK (fin_utc >= inicio_utc),
    CONSTRAINT ck_sesiones_duracion CHECK (duracion_configurada_s BETWEEN 30 AND 1800 AND duracion_real_s >= 0),
    CONSTRAINT ck_sesiones_estado   CHECK ((estado = 'completa') = (motivo_fin = 'completada'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Resultado propio de la caminata (una fila por sesión).
CREATE TABLE caminata_resultados (
    sesion_id         BIGINT UNSIGNED   NOT NULL,
    distancia_m       DECIMAL(6,2)      NOT NULL,
    largo_pista_m     DECIMAL(5,2)      NULL,                      -- NULL si no se colocó la flecha
    tiempo_detenido_s DECIMAL(6,1)      NOT NULL DEFAULT 0,
    num_paradas       SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (sesion_id),
    CONSTRAINT fk_caminata_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE,
    CONSTRAINT ck_caminata_valores CHECK (distancia_m BETWEEN 0 AND 2000 AND tiempo_detenido_s >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Distancia recorrida en cada minuto (minuto 1 a 6 en la prueba de 360 s).
CREATE TABLE caminata_minutos (
    sesion_id   BIGINT UNSIGNED  NOT NULL,
    minuto      TINYINT UNSIGNED NOT NULL,
    distancia_m DECIMAL(6,2)     NOT NULL,
    PRIMARY KEY (sesion_id, minuto),
    CONSTRAINT fk_minutos_sesion FOREIGN KEY (sesion_id) REFERENCES sesiones (id) ON DELETE CASCADE,
    CONSTRAINT ck_minutos_valores CHECK (minuto BETWEEN 1 AND 30 AND distancia_m >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Paciente ficticio para las pruebas.
INSERT INTO pacientes (codigo, es_prueba) VALUES ('P-000', TRUE);
