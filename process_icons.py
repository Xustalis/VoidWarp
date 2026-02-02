
import os
import sys
from PIL import Image, ImageDraw, ImageOps

def process_icons(source_path, output_dir):
    try:
        if not os.path.exists(output_dir):
            os.makedirs(output_dir)

        # Load Source
        img = Image.open(source_path).convert("RGBA")
        
        # 1. Remove White Background (Simple thresholding or floodfill)
        # Since the image is on white, we can try to make white transparent.
        # Better: Create a mask.
        data = img.getdata()
        new_data = []
        for item in data:
            # If closer to white, make transparent. 
            # This is a naive approach; strictly, we should probably crop to the circle if we know it's a circle.
            # Given the image is generated with a white background, let's assume top-left pixel is background.
            if item[0] > 240 and item[1] > 240 and item[2] > 240:
                new_data.append((255, 255, 255, 0))
            else:
                new_data.append(item)
        
        img.putdata(new_data)
        
        # Crop to content (remove extra transparent space)
        bbox = img.getbbox()
        if bbox:
            img = img.crop(bbox)

        # Save base transparent
        base_path = os.path.join(output_dir, "base_transparent.png")
        img.save(base_path)
        print(f"Saved base transparent icon to {base_path}")

        # --- WINDOWS ICO ---
        # Sizes: 16, 32, 48, 256
        ico_sizes = [(16, 16), (32, 32), (48, 48), (256, 256)]
        ico_path = os.path.join(output_dir, "app.ico")
        img.save(ico_path, format='ICO', sizes=ico_sizes)
        print(f"Saved Windows ICO to {ico_path}")

        # --- ANDROID LEGACY ---
        # Mipmap directories and sizes (px)
        # mdpi: 48, hdpi: 72, xhdpi: 96, xxhdpi: 144, xxxhdpi: 192
        android_sizes = {
            "mipmap-mdpi": 48,
            "mipmap-hdpi": 72,
            "mipmap-xhdpi": 96,
            "mipmap-xxhdpi": 144,
            "mipmap-xxxhdpi": 192
        }

        # Base Android Output Dir
        android_out = os.path.join(output_dir, "android")
        
        for folder, size in android_sizes.items():
            folder_path = os.path.join(android_out, folder)
            os.makedirs(folder_path, exist_ok=True)
            
            # Legacy Square (Launcher)
            # Use the circular icon but ensure it fits in the square.
            # Standard android icons often have padding. 
            resized = img.resize((size, size), Image.Resampling.LANCZOS)
            resized.save(os.path.join(folder_path, "ic_launcher.png"))
            
            # Round
            resized.save(os.path.join(folder_path, "ic_launcher_round.png"))
            
        print("Saved Android Legacy icons")

        # --- ANDROID ADAPTIVE ---
        # Reference: xxxhdpi is 108dp = 432px total size.
        # Foreground: 432x432, connect is in center 264px (66dp safe zone approx).
        full_size = 432
        icon_size = 264 
        
        # 1. Background
        # Pick dominant color from the image (or just Deep Purple)
        # Let's sample the center or edge.
        # Or hardcode a dark elegant color: #2D004E (Deep Purple)
        bg_color = (45, 0, 78, 255) 
        bg_img = Image.new("RGBA", (full_size, full_size), bg_color)
        
        # Save background (we only need one high-res, usually placed in mipmap-anydpi-v26 as xml referencing a drawable, 
        # but for simplicity we can put pngs in density folders or just use one drawable).
        # Best practice: Put pngs in mipmap-xxxhdpi etc. or drawable-nodpi.
        # Let's generate for each density for correctness.
        
        # Adaptive sizes (total size):
        # mdpi: 108px (48dp * 2.25? No. 1dp = 1px @ mdpi. Wait.)
        # mdpi (1x): 108 x 108 px ( 48dp displayed within 108dp canvas?? No.)
        # The adaptive icon size specs:
        # mdpi: 108x108 px
        # hdpi: 162x162 px
        # xhdpi: 216x216 px
        # xxhdpi: 324x324 px
        # xxxhdpi: 432x432 px
        
        adaptive_sizes = {
            "mipmap-mdpi": 108,
            "mipmap-hdpi": 162,
            "mipmap-xhdpi": 216,
            "mipmap-xxhdpi": 324,
            "mipmap-xxxhdpi": 432
        }
        
        for folder, size in adaptive_sizes.items():
            folder_path = os.path.join(android_out, folder)
            
            # Background
            bg = Image.new("RGBA", (size, size), bg_color)
            bg.save(os.path.join(folder_path, "ic_launcher_background.png"))
            
            # Foreground
            # Scale icon to approx 60% of canvas
            fg_icon_size = int(size * 0.6)
            fg_icon = img.resize((fg_icon_size, fg_icon_size), Image.Resampling.LANCZOS)
            
            fg_canvas = Image.new("RGBA", (size, size), (0,0,0,0))
            # Center it
            offset = (size - fg_icon_size) // 2
            fg_canvas.paste(fg_icon, (offset, offset))
            fg_canvas.save(os.path.join(folder_path, "ic_launcher_foreground.png"))
            
            # Monochrome
            # Convert fg_canvas to grayscale transparency mask
            # For simplicity: Take alpha of fg, make it white.
            r, g, b, a = fg_canvas.split()
            mono_fg = Image.merge("RGBA", (
                Image.new("L", (size, size), 255), # R
                Image.new("L", (size, size), 255), # G
                Image.new("L", (size, size), 255), # B
                a # Use original alpha
            ))
            mono_fg.save(os.path.join(folder_path, "ic_launcher_monochrome.png"))

        print("Saved Android Adaptive icons")
        
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python process_icons.py <source_image> <output_dir>")
    else:
        process_icons(sys.argv[1], sys.argv[2])
