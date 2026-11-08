#!/usr/bin/env bash
# Crea una CA propia y el certificado TLS del servidor para una IP de la red local.
# Uso: ./scripts/crear-certificados.sh 192.168.1.X
# Todo queda en secrets/tls/ (excluido de Git). La clave del servidor se reutiliza
# si ya existe, para que el pin de las gafas no cambie al renovar el certificado.
set -euo pipefail

IP="${1:-}"
if [[ ! "$IP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
  echo "Uso: $0 <IP del servidor en la red local>" >&2
  exit 1
fi

D="secrets/tls"
mkdir -p "$D"
chmod 700 secrets "$D"
umask 077

# 1. CA propia (solo la primera vez). Su clave privada nunca sale de esta carpeta.
if [[ ! -f "$D/ca.key" ]]; then
  openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$D/ca.key"
  openssl req -x509 -new -key "$D/ca.key" -sha256 -days 1825 \
    -subj "/CN=Rehab CA local" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign" \
    -out "$D/ca.crt"
fi

# 2. Clave del servidor (se reutiliza si existe: el pin no cambia)
if [[ ! -f "$D/servidor.key" ]]; then
  openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$D/servidor.key"
fi

# 3. Certificado del servidor para la IP indicada (vigencia 397 días)
[[ -f "$D/servidor.crt" ]] && chmod 600 "$D/servidor.crt"     # permite renovarlo
openssl req -new -key "$D/servidor.key" -subj "/CN=rehab-servidor" -out "$D/servidor.csr"
openssl x509 -req -in "$D/servidor.csr" -CA "$D/ca.crt" -CAkey "$D/ca.key" \
  -set_serial "0x$(openssl rand -hex 16)" -days 397 -sha256 \
  -extfile <(printf "subjectAltName=IP:%s\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=serverAuth\n" "$IP") \
  -out "$D/servidor.crt" 2>/dev/null
rm -f "$D/servidor.csr"

# Permisos: la clave de la CA solo para ti; Nginx (en el contenedor) debe leer cert y clave
chmod 600 "$D/ca.key"
chmod 444 "$D/ca.crt" "$D/servidor.crt" "$D/servidor.key"

# 4. Pin de la clave pública (SPKI SHA-256, Base64) para las gafas
PIN=$(openssl x509 -in "$D/servidor.crt" -pubkey -noout \
  | openssl pkey -pubin -outform der \
  | openssl dgst -sha256 -binary | openssl base64)

echo "Certificado creado para IP: $IP"
openssl x509 -in "$D/servidor.crt" -noout -enddate
echo "Pin SPKI (sha256/base64): $PIN"
