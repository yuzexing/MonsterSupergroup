"""Validate recovered evidence, without treating source bugs as collection failures."""
import argparse, json, pathlib, sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'docs/evidence/hellmaiden-attacks'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
compare=read(OUT/'capture-comparison.json'); recovered=read(OUT/'recovered-enemies.json')
parser=argparse.ArgumentParser()
parser.add_argument('--run',default='observe-final')
args=parser.parse_args()
reports=read(OUT/'observations.json');run=next(r for r in reports if r['run']==args.run)
checks={
    'independent-static-snapshots-identical':compare['identical'],
    '31-timeline-clips':compare['clips']==31,
    'all-field-reads-and-references-resolved':not compare['issues'],
    '8-identities-16-variants':len(recovered['enemies'])==8 and sum(len(e['variants']) for e in recovered['enemies'])==16,
    'original-run-completed':run['completed'],
    'no-probe-errors':not run['probeErrors'],
}
coverage=[]
for enemy in recovered['enemies']:
    episodes=[e for e in run['episodes'] if e['identity']==enemy['identity']]
    for variant in enemy['variants']:
        selected=[e for e in episodes if e['variant']==variant]
        contact=enemy['identity'] in ('Brotchi','Slime')
        cycles=sum(len(e['cycles']) for e in selected)
        complete_combos=sum(e['finalComboRecoveries'] for e in selected)
        deaths=sum(not e.get('end',{}).get('active',True) for e in selected)
        valid=bool(selected) and (bool(sum(sum(d.values()) for d in [e['damageDeltas'] for e in selected])) if contact else cycles>=3)
        if enemy['identity']=='Ghoul':valid=valid and complete_combos>=3
        if enemy['identity']=='LostSoul':valid=valid and deaths>=3
        key=enemy['identity']+':'+str(variant)
        coverage.append(dict(key=key,episodes=len(selected),completeCycles=cycles,completeCombos=complete_combos,naturalEnemyEnds=deaths,contact=contact,passed=valid))
checks['all-variant-mechanism-coverage']=all(c['passed'] for c in coverage)
rows=[json.loads(line) for line in (ROOT/run['originalEventsFile']).read_text(encoding='utf-8-sig').splitlines()]
born={};birth_checks=[];binding_differences=[]
database={(e['identity'],d['variant']):d['baseValues'] for e in recovered['enemies'] for d in e['databaseValues']}
for row in rows:
    d=row['data']
    if row['kind']=='birth':born[d['id']]=d
    if row['kind']=='fixture-enemy':
        b=born[d['id']];expected=database[d['identity'],d['variant']]
        valid=b['hp']==expected['hp'] and b['damage']==expected['damage'] and abs(b['speed']-expected['speed']*b['speedMultiplier'])<1e-4 and .9<=b['speedMultiplier']<=1.1
        birth_checks.append(dict(identity=d['identity'],variant=d['variant'],id=d['id'],reuse=b['reuse'],hp=b['hp'],damage=b['damage'],speed=b['speed'],xp=b['xp'],passed=valid))
    if row['kind']=='post-start-bindings':
        for binding in d['damage']:
            if not binding['sameStats']:binding_differences.append(dict(frame=row['frame'],id=d['id'],controllerDamage=born[d['id']]['damage'],binding=binding))
checks['all-birth-attributes-match-original-database']=all(b['passed'] for b in birth_checks)
protection=read(ROOT/'Logs/EnemyRecovery/input-verification.json')
checks['original-export-and-save-files-unchanged']=protection['unchanged']
timing_checks=[]
for episode in run['episodes']:
    for cycle in episode['cycles']:
        phases=[('recovery',cycle['recovery'],cycle['effective']['recovery'],cycle['recoveryStart'],cycle['end'])]
        for strike in cycle['strikes']:
            for phase,start,end,expected in [('warning','warningStart','attackStart','warning'),('active','attackStart','attackEnd','active')]:
                if end in strike:
                    phases.append((phase,strike[phase],strike['effective'][expected],strike[start],strike[end]))
        for phase,observed,expected,start,end in phases:
            frame_allowance=max(r['deltaTime'] for r in rows if start<=r['time']<=end)+1e-4
            timing_checks.append(dict(identity=episode['identity'],variant=episode['variant'],phase=phase,start=start,
                expected=expected,observed=observed,frameAllowance=frame_allowance,passed=expected-1e-4<=observed<=expected+frame_allowance))
checks['phase-times-within-observed-frame-boundaries']=bool(timing_checks) and all(t['passed'] for t in timing_checks)
flights=run['projectileFlights']
checks['imp-measured-projectile-speed-5']=len(flights)>=6 and all(abs(f.get('measuredSpeed',0)-5)<.01 for f in flights)
checks['imp-v0-v1-hit-damage-50-70']=all(e['damageDeltas']=={str(50 if e['variant']==0 else 70):4} for e in run['episodes'] if e['identity']=='Imp')
result=dict(checks=checks,coverage=coverage,birthChecks=birth_checks,observedStaleDamageBindings=binding_differences,
    timingChecks=timing_checks,
    scope='Original instrumented runtime mechanism evidence. Not MonsterSupergroup integration acceptance; not a full unmodified-game comparison.')
(OUT/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(checks,ensure_ascii=False,indent=2))
sys.exit(not all(checks.values()))
