# LEGOPATCHER

Emulador por software del **Toy Pad** de LEGO Dimensions para PS3. Pon figuras,
vehículos y gadgets en el juego sin el portal físico ni las figuras reales —
los eliges desde la app y aparecen en la consola al instante.

App multiplataforma en .NET MAUI: **Windows, Android, iOS y macOS**.

## Cómo funciona

Un plugin corre dentro del juego en la PS3 y finge ser el portal USB real. La
app le dice al plugin, por FTP (webMAN MOD), qué figura colocar en cada espacio.

## Requisitos

- PS3 con jailbreak (HEN 4.91+ o CFW) y **webMAN MOD** con FTP activo.
- Juego instalado en formato carpeta o ISO, **versión 1.22**.
- Solo edición **americana (BLUS31473)** por ahora.

## Uso

1. Activa el FTP en webMAN.
2. **Conectar** → IP local de la PS3.
3. **Instalar PS3** → sube el plugin + EBOOT parcheado (respaldo automático).
4. Abre el juego y coloca figuras: arrástralas a un espacio o toca espacio +
   figura. Arrastra entre espacios para mover/intercambiar.

Los volcados `.bin` de las figuras **no se distribuyen** (contenido del juego).
Cada usuario importa su propio `Dimensions.zip`; se guardan solo en el
dispositivo.

## Compilar

Local (Windows):
```
dotnet build -c Release -f net10.0-windows10.0.19041.0
```

Las demás plataformas se compilan en GitHub Actions (ver
`.github/workflows/build.yml`): Android en ubuntu, iOS/macOS en macOS runner.
Los .ipa/.app salen sin firmar (requieren tu certificado Apple para instalar).

## Créditos

- Desarrollo: **RadiantDelux** — plugin, parche del EBOOT, protocolo e interfaz.
- Protocolo: basado en node-ld / ToyPadEmu.
- Herramientas: TrueAncestor SELF Resigner, scetool, SPRXPatcher (mod), Cell SDK, webMAN MOD.
- Imágenes: LEGO Dimensions Wiki (Fandom).

LEGO y LEGO Dimensions son marcas de The LEGO Group. Proyecto no afiliado, sin
fines de lucro, para preservación y uso personal.
