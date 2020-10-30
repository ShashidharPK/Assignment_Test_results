var arr = [{
    "type": "test1",
    "id": "100",
    "values": {
        "name": "Alpha"
    },
    "validations": []
}, {
    "type": "services",
    "validations": [{
        "id": "200",
        "name": "Shashi",
        "selection": [{
            "id": "300",
            "values": {
                "name": "Blob"
            }
        }]
    }]
}];

function findProp(obj, key, out) {
    var i,
        proto = Object.prototype,
        ts = proto.toString,
        hasOwn = proto.hasOwnProperty.bind(obj);

    if ('[object Array]' !== ts.call(out)) out = [];

    for (i in obj) {
        if (hasOwn(i)) {
            if (i === key) {
                out.push(obj[i]);
            } else if ('[object Array]' === ts.call(obj[i]) || '[object Object]' === ts.call(obj[i])) {
                findProp(obj[i], key, out);
            }
        }
    }

    return out;
}

console.log(findProp(arr, "id"));
