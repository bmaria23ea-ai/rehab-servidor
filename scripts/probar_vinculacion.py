# Pruebas de la vinculación de visores (solicitar, aprobar en el servidor, consultar).
# Uso: URL=https://<IP>:<puerto>/api CA_FILE=secrets/tls/ca.crt python3 scripts/probar_vinculacion.py
# Simula unas gafas. El secreto que recibe no se muestra ni se guarda.
import json, os, ssl, urllib.request, urllib.error

URL, CA = os.environ["URL"], os.environ["CA_FILE"]
ctx = ssl.create_default_context(cafile=CA)

def post(ruta, cuerpo=None, crudo=None):
    datos = crudo if crudo is not None else (json.dumps(cuerpo).encode() if cuerpo is not None else b"")
    req = urllib.request.Request(URL + ruta, data=datos, method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, context=ctx) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def probar(nombre, esperado, resultado, mostrar=True):
    codigo, cuerpo = resultado
    marca = "OK " if codigo == esperado else "FALLA"
    print(f"[{marca}] {nombre}: {codigo} (esperado {esperado}) {cuerpo[:120] if mostrar else ''}")
    return codigo, cuerpo

c, cuerpo = probar("1. Solicitar vinculación", 200, post("/visores/solicitar"), mostrar=False)
solicitud = json.loads(cuerpo)
dispositivo = solicitud["codigoDispositivo"]
print(f"    Código para aprobar: {solicitud['codigo']}  (vence en {solicitud['expiraEn'] // 60} min)")

probar("2. Consultar antes de aprobar", 202, post("/visores/consultar", {"codigoDispositivo": dispositivo}))
probar("3. Identificador inexistente", 401, post("/visores/consultar", {"codigoDispositivo": "A" * 43}))
probar("4. Identificador mal formado", 400, post("/visores/consultar", {"codigoDispositivo": "abc"}))
probar("5. Campo extra no permitido", 400,
       post("/visores/consultar", {"codigoDispositivo": dispositivo, "visorId": 1}))

input(f'\n    Apruebe ahora en otra terminal:\n'
      f'    docker compose exec api dotnet Rehab.Api.dll visor-aprobar {solicitud["codigo"]} "Prueba vinculacion"\n'
      f'    y luego pulse Enter aquí... ')

c, cuerpo = probar("6. Consultar después de aprobar", 200,
                   post("/visores/consultar", {"codigoDispositivo": dispositivo}), mostrar=False)
if c == 200:
    print(f"    Vinculado como visor #{json.loads(cuerpo)['visorId']} (secreto recibido, no se muestra)")
probar("7. Segunda consulta (un solo uso)", 401, post("/visores/consultar", {"codigoDispositivo": dispositivo}))
