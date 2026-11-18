"""Package the approved rounded cutouts; originals remain in Images/Brand."""
from pathlib import Path
from PIL import Image

images = Path(__file__).resolve().parent.parent / 'Plain Craft Launcher 2' / 'Images'
launcher = Image.open(images / 'Brand' / 'LauncherSelected.png').convert('RGBA')
server = Image.open(images / 'Brand' / 'ServerRounded.png').convert('RGBA')
launcher.resize((512, 512), Image.Resampling.LANCZOS).save(images / 'LauncherLogo.png')
launcher.resize((128, 128), Image.Resampling.LANCZOS).save(images / 'LauncherSplash.png')
server.resize((256, 256), Image.Resampling.LANCZOS).save(images / 'BrsServerLogo.png')
launcher.save(images / 'icon.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print('Prepared launcher logo, splash, multiresolution icon, and separate BRS server logo.')
