#!/usr/bin/env python3
"""Import the verified, version-pinned 1.18/1.24 text and resource profiles.

Run in the unified launcher checkout. The existing root text contract remains
the 1.26 stable channel; legacy text is separately keyed and never merged into
its history. This script never writes to source packages or game directories.
"""
from pathlib import Path
import argparse
import copy
import hashlib
import json
import shutil
import sys

sys.dont_write_bytecode = True
from lng import LNG

ROOT = Path(__file__).resolve().parent
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--legacy-root', type=Path,
                        default=ROOT.parent / 'f1_legacy_support_20261003')
    args = parser.parse_args()
    manifest_path = ROOT / 'manifest.base.json'
    manifest = json.loads(manifest_path.read_text('utf-8-sig'))
    assert manifest['schema'] == 24
    stable_fields = ('lng', 'previous_lng', 'translation_variants', 'text_version')
    stable_before = {key: copy.deepcopy(manifest[key]) for key in stable_fields}
    evidence = ROOT / 'evidence' / 'engine-resources'
    evidence.mkdir(parents=True, exist_ok=True)
    original = evidence / 'ORIGINAL_manifest.base.json'
    if not original.exists():
        shutil.copy2(manifest_path, original)
    original_engine = evidence / 'ORIGINAL_Engine.ps1'
    if not original_engine.exists():
        shutil.copy2(ROOT / 'Engine.ps1', original_engine)
    profiles = [p for p in manifest['native_profiles'] if not p.get('language_set')]
    assert len(profiles) == 2, 'Expected the two existing 1.26 profiles'
    for p in profiles:
        p['game_version'] = '1.26'
    manifest['version'] = '0.29'
    manifest['language_records'] = 56134
    manifest['language_sets'] = {}
    source_hashes = {}
    imported = []
    for version, build, records in (('1.18', '118', 56439), ('1.24', '124', 55991)):
        package = args.legacy_root.resolve() / ('build' + build) / ('F1_25_RU_' + version)
        source_manifest_path = package / 'manifest.json'
        source_hashes[str(source_manifest_path)] = sha(source_manifest_path)
        src = json.loads(source_manifest_path.read_text('utf-8-sig'))
        assert src['legacy_game_version'] == version and src['language_records'] == records
        assert src['updates_disabled'] is True and len(src['native_profiles']) == 1
        profile = copy.deepcopy(src['native_profiles'][0])
        assert not profile.get('micro_compatibility') and profile['accepted_dat'] == []
        assert profile['protected'] == {'F1_25.exe': [profile['exe_sha256']]}
        assert profile['backup_dir'] == '.f1ru-v25-' + profile['dat_original']
        profile['language_set'] = version
        profile['game_version'] = version
        # Keep logical install_files.payload=language.lng: Engine resolves its
        # physical file only after selecting this profile's language set.
        assert sum(f['payload'] == 'language.lng' for f in profile['install_files']) == 2
        for name in ('fonts_russian.erp', 'fonts_russian_r_p.erp'):
            assert src['payload'][name] == manifest['payload'][name]
            assert sha(package / 'payload' / name) == manifest['payload'][name]
        russian_name = 'language-' + version + '.lng'
        names_name = 'language-' + version + '-original-names.lng'
        mappings = [('language.lng', russian_name), ('language-original-names.lng', names_name)]
        for old_name, new_name in mappings:
            source = package / 'payload' / old_name
            digest = sha(source)
            assert digest == src['payload'][old_name]
            assert len(LNG.read(source).rows) == records
            source_hashes[str(source)] = digest
            destination = ROOT / 'payload' / new_name
            if destination.exists():
                assert sha(destination) == digest, 'Refusing to overwrite different payload: ' + new_name
            else:
                shutil.copy2(source, destination)
            assert sha(destination) == digest
            manifest['payload'][new_name] = digest
        variant = copy.deepcopy(src['translation_variants']['original_names'])
        assert variant['records'] == records and variant['sha256'] == src['payload']['language-original-names.lng']
        variant['payload'] = names_name
        manifest['language_sets'][version] = {
            'records': records,
            'version': src['text_version'],
            'lng': src['lng'],
            'previous_lng': copy.deepcopy(src['previous_lng']),
            'payload': russian_name,
            'translation_variants': {'original_names': variant},
        }
        assert src['lng'] == src['payload']['language.lng']
        profiles.append(profile)
        imported.append({'game_version': version, 'records': records, 'exe_sha256': profile['exe_sha256'],
                         'dat_original': profile['dat_original'], 'dat_installed': profile['dat_installed'],
                         'payloads': [russian_name, names_name]})
    assert all(manifest[key] == value for key, value in stable_before.items())
    manifest['native_profiles'] = profiles
    assert len({p['exe_sha256'] for p in profiles}) == 4
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    assert json.loads(manifest_path.read_text('utf-8')) == manifest
    assert all(sha(Path(path)) == digest for path, digest in source_hashes.items())
    report = {'source_hashes_unchanged': source_hashes, 'imported': imported,
              'root_current_text_contract_unchanged': True,
              'manifest_sha256': sha(manifest_path), 'profiles': 4, 'payload_files': len(manifest['payload'])}
    (evidence / 'IMPORT_VERIFICATION.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('IMPORT_PASS profiles=4 legacy_sets=2 payload_files=8 current_text_contract_unchanged=true sources_unchanged=true')


if __name__ == '__main__':
    main()
