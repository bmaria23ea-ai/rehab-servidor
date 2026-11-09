# Pruebas de POST /api/sesiones simulando un visor. Uso: python3 probar_sesiones.py
# Lee URL, CA y TOKEN de variables de entorno. No guarda nada en disco.
import json, os, ssl, uuid, urllib.request, urllib.error
from datetime import datetime, timedelta, timezone

URL, CA, TOKEN = os.environ["URL"], os.environ["CA_FILE"], os.environ["TOKEN"]
ctx = ssl.create_default_context(cafile=CA)

def enviar(cuerpo, token=TOKEN, crudo=None):
    datos = crudo if crudo is not None else json.dumps(cuerpo).encode()
    req = urllib.request.Request(URL + "/sesiones", data=datos, method="POST",
                                 headers={"Content-Type": "application/json"})
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, context=ctx) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def sesion(paciente="P-001", estado="completa", motivo="completada", real=360.0, minutos=None, es_prueba=False):
    fin = datetime.now(timezone.utc) - timedelta(minutes=1)
    inicio = fin - timedelta(seconds=real)
    minutos = minutos if minutos is not None else [55.1, 54.0, 52.3, 51.0, 50.2, 49.8][: max(1, int(real // 60))]
    z = lambda d: d.strftime("%Y-%m-%dT%H:%M:%S.") + f"{d.microsecond // 1000:03d}Z"
    return {
        "versionEsquema": 1, "sesionId": str(uuid.uuid4()), "paciente": paciente,
        "ejercicio": "caminata_6m", "escena": "f-caminata reducido", "versionApp": "0.1.0",
        "esPrueba": es_prueba, "inicioUtc": z(inicio), "finUtc": z(fin),
        "duracionConfiguradaS": 360, "duracionRealS": real, "estado": estado, "motivoFin": motivo,
        "caminata": {"distanciaM": round(sum(minutos), 2), "largoPistaM": 9.5,
                     "tiempoDetenidoS": 12.0, "numParadas": 2, "distanciaPorMinutoM": minutos},
    }

def probar(nombre, esperado, resultado):
    codigo, cuerpo = resultado
    marca = "OK " if codigo == esperado else "FALLA"
    print(f"[{marca}] {nombre}: {codigo} (esperado {esperado}) {cuerpo[:140]}")

s1 = sesion()
probar("1. Sesión completa válida", 201, enviar(s1))
probar("2. Reenvío de la misma sesión", 200, enviar(s1))
probar("3. Incompleta de P-000 (salió al menú)", 201,
       enviar(sesion("P-000", "incompleta", "salida_menu", 150.0, [55.1, 54.0])))
probar("4. Paciente no registrado", 422, enviar(sesion("P-555")))
probar("5. 'completa' con motivo 'salida_menu'", 422, enviar(sesion(motivo="salida_menu")))
s6 = sesion(); s6["nombre"] = "Juan"
probar("6. Campo extra no permitido", 400, enviar(s6))
probar("7. Sin token", 401, enviar(sesion(), token=None))
probar("8. JSON roto", 400, enviar(None, crudo=b'{"versionEsquema":1,'))
print("sesionId de la prueba 1:", s1["sesionId"])
