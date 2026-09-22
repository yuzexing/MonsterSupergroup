import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('diagnostics',Path(__file__).parents[1]/'Analyze-NetworkDiagnostics.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class DiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
    def tearDown(self):self.temp.cleanup()
    def write(self,name,rows,header=None,complete=True,tail=''):
        p=self.root/name
        p.write_text('\n'.join(json.dumps(x) for x in [header or {'kind':'header','buildGuid':'abc','schemaVersion':2},*rows])+ '\n'+tail,encoding='utf-8')
        Path(str(p)+'.status.json').write_text(json.dumps({'complete':complete}),encoding='utf-8')
        return p
    def row(self,t,mean=10,**kw):
        r=dict(kind='sample',time=t,networkTime=t,run='run',round=1,role='client',frameCount=10,
               frameMeanMs=mean,frameMaxMs=mean*2,frameP95Ms=mean,frameP99Ms=mean,alive=3,
               sentBytes=[int(t*100),0],receivedBytes=[0,int(t*200)],rejections=[])
        r.update(kw);return r
    def test_legacy_missing_metrics_stay_unknown(self):
        p=self.write('old.jsonl',[self.row(1),self.row(2)],{'kind':'header'})
        result=m.summarize(p)['runs'][0]
        self.assertIsNone(result['maxQueueMs']);self.assertIsNone(result['longFrames'])
        self.assertEqual(result['receivedBytesPerSecond'],[0,200])
    def test_weighted_mean_is_not_average_of_percentiles(self):
        p=self.write('new.jsonl',[self.row(1,10,frameCount=100),self.row(2,100,frameCount=1)])
        r=m.summarize(p)['runs'][0]
        self.assertAlmostEqual(r['frameMeanMs'],1100/101)
        self.assertEqual(r['worstWindowP95Ms'],100)
    def test_crash_tail_retains_prior_samples_and_marks_incomplete(self):
        p=self.write('crash.jsonl',[self.row(1)],complete=False,tail='{"kind":')
        r=m.summarize(p)
        self.assertFalse(r['evidenceComplete']);self.assertEqual(r['malformedLines'],1)
        self.assertEqual(r['runs'][0]['samples'],1)
    def test_alignment_uses_network_clock_not_process_uptime(self):
        a=self.write('a.jsonl',[self.row(100,80,networkTime=1000)])
        b=self.write('b.jsonl',[self.row(900,10,networkTime=1000.3,role='host')])
        match=m.align([a,b])['slowWindows'][0]
        self.assertEqual(len(match['peers']),1)
        self.assertAlmostEqual(match['peers'][0]['networkTimeDifferenceSeconds'],.3)
    def test_different_build_or_round_cannot_be_paired(self):
        a=self.write('a.jsonl',[self.row(1,80)])
        b=self.write('b.jsonl',[self.row(1)],{'kind':'header','buildGuid':'other'})
        c=self.write('c.jsonl',[self.row(1,round=2)])
        self.assertEqual(m.align([a,b,c])['slowWindows'][0]['peers'],[])
    def test_correlations_report_measured_signals_without_root_cause(self):
        p=m.point(self.row(2,80,warnings=150,gcMs=8,connections=[{'queueMilliseconds':250}]),self.row(1,warnings=0))
        self.assertEqual(p['signals'],['gc','warning-storm','network-queue'])
        self.assertNotIn('cause',p)
    def test_rounds_are_not_aggregated_and_timeline_can_be_exported(self):
        p=self.write('a.jsonl',[self.row(1),self.row(2,round=2)])
        self.assertEqual(len(m.summarize(p)['runs']),2)
        m.write_timeline([p],self.root/'timeline.csv')
        self.assertEqual(len((self.root/'timeline.csv').read_text(encoding='utf-8-sig').splitlines()),3)
    def test_queue_and_throughput_changes_remain_visible(self):
        point=m.point(self.row(2,connections=[{'pendingReliableBytes':200,'unacknowledgedBytes':400}]), self.row(1))
        self.assertEqual(point['pendingReliableBytes'],200)
        self.assertEqual(point['unacknowledgedBytes'],400)
        self.assertEqual(point['receivedBytesPerSecond'],200)
        self.assertIsNone(point['pendingUnreliableBytes'])

if __name__=='__main__':unittest.main()
