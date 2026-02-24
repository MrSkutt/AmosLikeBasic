from PIL import Image

img = Image.open("bild.png").convert("RGBA")
datas = img.getdata()

newData = []
for item in datas:
    # om färgen är svart
    if item[0] < 50 and item[1] < 50 and item[2] < 50:
        newData.append((255, 255, 255, 0))  # gör transparent
    else:
        newData.append(item)

img.putdata(newData)
img.save("output.png")
