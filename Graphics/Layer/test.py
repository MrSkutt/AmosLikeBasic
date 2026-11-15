from PIL import Image

img = Image.open("bild.png").convert("RGBA")
datas = img.getdata()

newData = []
for item in datas:
    # if the colour is black
    if item[0] < 50 and item[1] < 50 and item[2] < 50:
        newData.append((255, 255, 255, 0))  # make it transparent
    else:
        newData.append(item)

img.putdata(newData)
img.save("output.png")
