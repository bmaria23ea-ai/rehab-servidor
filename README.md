# Servidor de sesiones de rehabilitación en RV

Servidor que recibe y guarda las sesiones de rehabilitación enviadas por los visores Meta Quest 3 de la plataforma de realidad virtual. Por ahora registra la prueba de caminata de 6 minutos; después se agregarán los ejercicios del gimnasio y de respiración.

Los pacientes se identifican solo con un código seudónimo (`P-001`). El sistema nunca guarda nombres ni otros datos personales.

Los terapeutas revisan las sesiones desde un portal web, también desde el celular.

## Arquitectura

| Servicio | Imagen | Función |
|---|---|---|
| `proxy` | `nginxinc/nginx-unprivileged:1.30-alpine` | Único servicio publicado. HTTPS con TLS 1.3 y 1.2 (solo suites ECDHE con AES-GCM o ChaCha20), límite de tasa y cabeceras de seguridad |
| `api` | Se construye desde `src/Rehab.Api` (ASP.NET Core 10) | Vinculación de visores, tokens JWT, asignación de pacientes y recepción de sesiones |
| `portal` | Se construye desde `src/Rehab.Portal` (ASP.NET Core 10, Blazor SSR) | Portal de terapeutas: sesiones, gafas y pacientes |
| `db` | `mariadb:12.3` | Base de datos con cifrado en reposo. No se publica fuera de Docker |

La base de datos, la API y el portal viven en una red interna de Docker sin salida a internet. Todas las contraseñas y claves se entregan como Docker secrets y nunca se guardan en el repositorio.

## Requisitos

- Docker Engine con el plugin Compose (probado con Docker 29.9 y Compose 5.7 en Ubuntu 24.04).
- SDK de .NET 10, solo para compilar fuera de Docker.
- OpenSSL y Python 3, para generar secretos y ejecutar las pruebas.
- [age](https://github.com/FiloSottile/age) (`sudo apt install age`), para cifrar las copias de seguridad.

## Puesta en marcha

Todos los comandos se ejecutan en la carpeta del proyecto.

**1. Generar los secretos** (quedan en `secrets/`, excluida de Git):

```bash
mkdir -p secrets && chmod 700 secrets
openssl rand -base64 32 | tr -d '\n' > secrets/db_root_password.txt
openssl rand -base64 32 | tr -d '\n' > secrets/db_app_password.txt
openssl rand -base64 32 | tr -d '\n' > secrets/db_portal_password.txt
echo "1;$(openssl rand -hex 32)" > secrets/mariadb_keyfile.txt
openssl rand -base64 64 | tr -d '\n' > secrets/jwt_key.txt
chmod 444 secrets/*.txt
```

**2. Crear la CA local y el certificado del servidor** para la IP que usarán los visores:

```bash
./scripts/crear-certificados.sh <IP-del-servidor>
```

El script muestra el pin SPKI de la clave del servidor, que la app de Unity usa para verificar que se conecta al servidor correcto.

**3. Elegir los puertos** (opcional; API en 443 y portal en 8444 por defecto):

```bash
cp .env.example .env    # HTTPS_PORT y PORTAL_PORT
```

**4. Levantar los servicios:**

```bash
docker compose up -d --build
docker compose ps       # los servicios deben aparecer "healthy"
```

La primera vez, MariaDB ejecuta los scripts de `db/init/` en orden y crea el esquema.

> **Importante:** si se pierde `secrets/mariadb_keyfile.txt`, los datos cifrados no se pueden recuperar. Guardar una copia segura fuera del servidor.

## Administración

Los comandos se ejecutan dentro del contenedor de la API. No están disponibles desde la red.

| Comando | Función |
|---|---|
| `visor-aprobar <código> "<nombre>"` | Vincula las gafas que muestran ese código (vence a los 10 minutos) |
| `visor-revocar <id>` | Bloquea un visor de inmediato |
| `visor-lista` | Lista los visores y su estado |
| `paciente-nuevo <código> <AAAA-MM-DD> <versión>` | Registra un paciente real con la fecha y versión de su consentimiento |
| `paciente-prueba <código>` | Registra un paciente ficticio para pruebas |
| `paciente-lista` | Lista los pacientes |
| `asignacion-nueva <visor> <paciente> [min]` | Asigna un paciente a un visor (vence a los 30 minutos sin sesiones; cada sesión recibida la renueva) |
| `asignacion-quitar <visor>` | Quita la asignación del visor |
| `asignacion-lista` | Lista las asignaciones vigentes y vencidas |
| `sesion-sin-asignar` | Lista las sesiones que llegaron sin paciente |
| `sesion-asignar <sesionId> <paciente>` | Asigna un paciente a una sesión que llegó sin él |

Ejemplo:

```bash
docker compose exec api dotnet Rehab.Api.dll visor-aprobar K7QM-4XPA "Quest 3 - 01"
```

### Vincular un visor

1. En las gafas, pulsar **Vincular visor**: aparece un código.
2. En el servidor, aprobarlo con `visor-aprobar`.
3. Las gafas se vinculan solas en unos segundos. Solo se hace una vez; desinstalar la app obliga a repetirlo.

### Migraciones

En una instalación nueva, MariaDB aplica `db/init/` en orden. En una base existente, cada migración nueva se aplica una vez con root:

```bash
docker compose exec -T db sh -c 'MYSQL_PWD="$(cat /run/secrets/db_root_password)" mariadb -uroot rehab' < db/init/0X_nombre.sql
```

## Portal de terapeutas

Se abre en `https://<IP-del-servidor>:8444`. El dispositivo debe tener instalada la CA local (`secrets/tls/ca.crt`).

| Página | Función |
|---|---|
| Sesiones | Sesiones por paciente o sin asignar, con el detalle minuto a minuto |
| Gafas | Vincular gafas, asignarles un paciente y desactivarlas |
| Pacientes | Registrar pacientes, descargar sus datos (CSV para Excel) y eliminarlos con todas sus sesiones |

Las cuentas se crean desde la terminal del servidor; el sistema muestra una contraseña temporal que se cambia al primer ingreso:

| Comando | Función |
|---|---|
| `terapeuta-nuevo <usuario>` | Crea la cuenta con contraseña temporal |
| `terapeuta-restablecer <usuario>` | Nueva contraseña temporal y desbloqueo |
| `terapeuta-cerrar-sesiones <usuario>` | Cierra la sesión en todos sus equipos |
| `terapeuta-desactivar <usuario>` / `terapeuta-activar <usuario>` | Bloquea o habilita la cuenta |
| `terapeuta-lista` | Lista las cuentas |

```bash
docker compose exec portal dotnet Rehab.Portal.dll terapeuta-nuevo mlopez
```

## API

Todas las rutas se publican bajo `/api/` a través de Nginx.

| Método y ruta | Autenticación | Función |
|---|---|---|
| `POST /api/visores/solicitar` | Ninguna | Crea una solicitud de vinculación y devuelve el código a mostrar |
| `POST /api/visores/consultar` | Identificador de la solicitud | `202` mientras no se aprueba; `200` con el secreto del visor, una sola vez |
| `POST /api/token` | Id y secreto del visor | Entrega un JWT válido por 15 minutos |
| `GET /api/asignacion` | JWT | Paciente asignado al visor (200) o ninguno (204) |
| `POST /api/sesiones` | JWT | Recibe una sesión (formato `versionEsquema: 1`). Con `"paciente": null` queda sin asignar |
| `GET /api/visores/yo` | JWT | Prueba de autenticación |
| `GET /api/salud` | Ninguna | Comprueba que la API llega a la base de datos |

Las sesiones se identifican con un `sesionId` generado en el visor: si el visor reenvía una sesión ya guardada, el servidor responde `200` con estado `duplicada` y no la guarda dos veces.

Las sesiones guardan la duración en segundos; `duracion_configurada_min` y `duracion_real_min` la muestran en minutos (columnas calculadas).

## Copias de seguridad

`scripts/respaldo.sh` exporta la base con `mariadb-dump`, la comprime y la cifra con age usando solo la llave pública; la copia nunca queda sin cifrar en disco. Guarda una copia local y otra externa, y borra las de más de `RESPALDO_DIAS` días.

**Llaves (una sola vez):**

```bash
umask 077
age-keygen -o ~/llave_privada_respaldos.txt
age-keygen -y ~/llave_privada_respaldos.txt > secrets/respaldo_publica.txt
```

La llave privada se guarda fuera del servidor (USB y gestor de contraseñas) y se borra del servidor. Sin ella las copias no se pueden abrir.

**Variables en `.env`:**

| Variable | Uso | Por defecto |
|---|---|---|
| `RESPALDO_LOCAL` | Carpeta local | `~/respaldos-rehab` |
| `RESPALDO_DESTINO` | Carpeta externa (p. ej. OneDrive) | sin copia externa |
| `RESPALDO_DIAS` | Días de retención | `30` |

**Uso:**

```bash
./scripts/respaldo.sh
./scripts/probar_restauracion.sh /ruta/llave_privada_respaldos.txt
```

`probar_restauracion.sh` descifra la copia más reciente en un MariaDB temporal en RAM y sin red, compara las filas de cada tabla con la base actual y lo elimina al terminar.

Los pacientes eliminados siguen en las copias anteriores hasta que caducan; con la retención por defecto desaparecen de todas en 30 días.

## Pruebas

| Script | Prueba |
|---|---|
| `scripts/probar_vinculacion.py` | Solicitud, aprobación y consulta de la vinculación |
| `scripts/probar_sesiones.py` | Recepción y validación de sesiones (requiere `TOKEN`) |

```bash
URL=https://<IP>:<puerto>/api CA_FILE=secrets/tls/ca.crt python3 scripts/probar_vinculacion.py
URL=https://<IP>:<puerto>/api CA_FILE=secrets/tls/ca.crt TOKEN=<jwt> python3 scripts/probar_sesiones.py
```

## Seguridad

- Contraseñas, claves y certificados solo como Docker secrets en `secrets/`, excluida de Git.
- MariaDB con todas las tablas cifradas (`innodb_encrypt_tables = FORCE`) y un usuario de aplicación con permisos mínimos.
- Contenedores con sistema de archivos de solo lectura, sin privilegios de Linux y con memoria limitada.
- Códigos de vinculación y secretos de los visores guardados solo como hash SHA-256; el secreto se entrega una sola vez.
- Comparación en tiempo constante, límite de tasa por IP, máximo de solicitudes de vinculación activas y revocación inmediata de visores.
- Validación estricta de cada campo de las sesiones; los campos desconocidos se rechazan.
- Portal: contraseñas de 10 caracteres o más según NIST SP 800-63B-4, con hash PBKDF2 de ASP.NET Core Identity; bloqueo de 15 minutos tras 5 intentos y límite de tasa en el ingreso.
- Sesión de 30 días en cookie `__Host-` (solo HTTPS, `HttpOnly`, `SameSite=Strict`); cambiar la contraseña o desactivar la cuenta la cierra en todos los equipos.
- Token antifalsificación en cada formulario, CSP que solo admite scripts propios y `Cache-Control: no-store` en las páginas con datos.
- Registro de auditoría de solo inserción (triggers impiden modificarlo o borrarlo), visible en el portal.
- Copias de seguridad cifradas con age; el servidor solo tiene la llave pública.
- Sin códigos de pacientes en los registros: Nginx no guarda accesos del portal y el portal solo registra advertencias.
- Nginx vuelve a resolver los nombres de los contenedores con el DNS de Docker cuando se recrean.
