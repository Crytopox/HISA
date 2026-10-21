// Run with Node 22+: node generate-blue-marshal.mjs /path/to/unpacked/package
// Independent oracle: @truebrain/blue-marshal 1.0.1, registry tarball SHA-512:
// u45r9c8SG4fSJ50xdr8n0EHhpK4uwQKN+qZXyt4jvMhe8/ZK7qqOBEk55FK9p6QxflO2oZQd4aq0pDmr7QHYuw==
import { readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { deepStrictEqual } from 'node:assert';
const directory = resolve(process.argv[2]);
const glue = await import(pathToFileURL(join(directory, 'blue_marshal_wasm_bg.js')).href);
const module = await WebAssembly.compile(await readFile(join(directory, 'blue_marshal_wasm_bg.wasm')));
const instance = await WebAssembly.instantiate(module, { './blue_marshal_wasm_bg.js': glue });
glue.__wbg_set_wasm(instance.exports);
instance.exports.__wbindgen_start();
if (process.argv[3]) {
  const fixture = JSON.parse(await readFile(fileURLToPath(new URL('blue-marshal-v1.json', import.meta.url)), 'utf8'));
  const actual = JSON.parse(glue.decode_to_json(await readFile(resolve(process.argv[3]))));
  deepStrictEqual(actual, fixture.expected);
  console.log('Rust/WASM independently decoded C# output with identical typed data.');
  process.exit(0);
}
const values = {
  'bytes:ui': {
    'bytes:probescanning.customFormations': { tuple: ['long:134280801871504062', {
      'int:-4': { tuple: ['bytes:tempFormation', []] },
      'int:7': { tuple: ['utf8:Independent 雪', [{ tuple: [{ tuple: [123456.789123, -500000.25, 0.125] }, 37399467675] }]] }
    }] },
    'bytes:probescanning.selectedFormationID': { tuple: ['long:134280801871504062', 7] },
    'bytes:unrelated': { tuple: [null, true, false, 'long:-123456789012345678901234567890', 'bytes:b64:/wCA', 'utf8:Snow ☃ 😀', 'global:example.Data',
      { instance: { class: 'example.Data', state: { 'int:5': 'bytes:state' } } },
      { callback: ['bytes:callback', 1.25] },
      { reduce: { newobj: false, callable: 'global:example.build', args: { tuple: [1, 'bytes:arg'] }, state: { 'bytes:flag': true }, list_items: [2], dict_items: [['bytes:key', 3]] } },
      { reduce: { newobj: true, callable: null, args: { tuple: ['global:example.Data'] }, state: null, list_items: [], dict_items: [] } }
    ] }
  },
  'json:{"tuple":[1,"utf8:key"]}': 'float:nan'
};
// Use oracle's own decoded form as the fixture's expected lossless representation.
const bytes = glue.encode_from_json(JSON.stringify(values));
const output = { package: '@truebrain/blue-marshal@1.0.1', base64: Buffer.from(bytes).toString('base64'), expected: JSON.parse(glue.decode_to_json(bytes)) };
await writeFile(fileURLToPath(new URL('blue-marshal-v1.json', import.meta.url)), JSON.stringify(output, null, 2) + '\n');
// Version 0 and shared/table fixtures originate in the upstream decoder's documented wire layout.
// Validate all hand-assembled wire fixtures independently with the Rust/WASM decoder.
const wire = {
  table: [125,1,17,1],
  v1shared: [125,1,44,83,2,104,105,27,0],
  v0shared: [126,1,0,0,0,44,83,2,104,105,27,1,1,0,0,0],
  v0crc: [126,0,0,0,0,28,10,0,10,0,9],
  unicode16: [125,1,18,2,104,0,233,0]
};
const fixtures = Object.entries(wire).map(([name, data]) => { const b = Uint8Array.from(data); return { name, base64: Buffer.from(b).toString('base64'), expected: JSON.parse(glue.decode_to_json(b)) }; });
await writeFile(fileURLToPath(new URL('blue-marshal-wire.json', import.meta.url)), JSON.stringify(fixtures, null, 2) + '\n');
console.log('Generated and independently decoded', 1 + fixtures.length, 'fixtures.');
