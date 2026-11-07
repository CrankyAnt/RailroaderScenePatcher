import json

func modifyMap(map) {
    map.merge(json.load("Data/Map.json"))
}
