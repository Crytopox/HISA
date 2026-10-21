# Probe Formation Designer references

The managed Blue Marshal codec and embedded string table are ported from
[TrueBrain/blue-marshal-rs](https://github.com/TrueBrain/blue-marshal-rs), commit
`fa99764271f5a70a9565ab6e6f092f57617382a8`. Its wire format and string table
originate in CCP Games' blue.Marshal implementation. Both MIT notices are
included in `src/Hisa.Services/Probes/BLUE-MARSHAL-LICENSE.txt` and copied into
published application output under `Probes/`.

EVE's account formation layout, units and settings discovery were investigated
using [EVE Wrench](https://github.com/eve-wrench/eve-wrench-app), commit
`2de3d8000e093b484ce11c36e18b635457352f75`, particularly `formations.rs`,
`locations.rs`, `scan.rs`, and the formation editor model. The HISA editor,
library and transactional settings service are independently implemented.

Independent binary fixtures were generated and validated using the published
`@truebrain/blue-marshal@1.0.1` Rust/WASM package. The fixture-generation script
records the package version and SHA-512 integrity value; the package itself is
not a HISA runtime dependency.
