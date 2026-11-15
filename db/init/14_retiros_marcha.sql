-- Marcha: retirar el visor ya no corta la prueba; se cuentan los retiros.
-- NULL = sesión anterior al cambio (dato no medido).
ALTER TABLE caminata_resultados
    ADD COLUMN retiros_visor SMALLINT UNSIGNED NULL AFTER num_paradas,
    ADD CONSTRAINT ck_caminata_retiros CHECK (retiros_visor <= 1000);

INSERT INTO esquema_migraciones (version, descripcion) VALUES (14, 'Retiros del visor en la marcha');
