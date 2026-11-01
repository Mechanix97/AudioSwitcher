# AudioSwitcher

Le da una segunda función a la tecla **Mute** del teclado en Windows:

- **Toque corto** → mutea/desmutea como siempre.
- **Mantener presionada** (~0,5 s) → cambia la salida de audio predeterminada a la siguiente y muestra un cartel en pantalla con el nombre.

No tiene ventana ni ícono en la bandeja: corre en segundo plano.

## Requisitos

Windows 10/11. No hace falta instalar nada: se compila con el `csc.exe` de .NET Framework 4 que ya viene con Windows.

## Compilar

```
build.cmd
```

Genera `AudioSwitcher.exe` en la misma carpeta.

## Uso

| Comando | Qué hace |
|---|---|
| `AudioSwitcher.exe` | Lo arranca en segundo plano (si ya está corriendo, no hace nada) |
| `AudioSwitcher.exe --exit` | Cierra la instancia que está corriendo |
| `AudioSwitcher.exe --list` | Lista las salidas de audio (`*` = la actual) |

### Arrancar con Windows

```powershell
$s = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\AudioSwitcher.lnk")
$s.TargetPath = "<ruta>\AudioSwitcher.exe"; $s.Save()
```

## Configuración

`config.ini`, al lado del `.exe` (los cambios se aplican al reiniciar el programa):

```ini
# Milisegundos que hay que mantener Mute para cambiar de salida
holdMs=500

# Salidas que se saltean al ciclar (parte del nombre). Una por línea.
exclude=Steam Streaming

# 1 = registra en audioswitcher.log los códigos de las teclas especiales
# (no letras/números). Útil si la tecla no responde.
debug=0
```

## Cómo funciona

- Un hook de teclado de bajo nivel (`WH_KEYBOARD_LL`) intercepta `VK_VOLUME_MUTE`.
- Si se suelta antes de `holdMs`, reinyecta la tecla para que Windows haga el mute normal (con su OSD). Las pulsaciones propias se marcan con `dwExtraInfo` en lugar de usar el flag `LLKHF_INJECTED`, porque algunos teclados (p. ej. Lenovo) entregan el Mute físico ya marcado como inyectado.
- Para cambiar la salida usa la interfaz COM no documentada `IPolicyConfig` (la misma que usan todos los switchers de audio), en los tres roles: consola, multimedia y comunicaciones.

## Limitaciones

- Si un juego corre como administrador, el hook no recibe la tecla mientras ese juego tiene el foco (hay que correr AudioSwitcher como admin también).
- El cartel no se ve sobre juegos en pantalla completa exclusiva (sí en ventana sin bordes).
