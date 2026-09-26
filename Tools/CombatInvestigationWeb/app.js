"use strict";
const $=id=>document.getElementById(id);
const labels={traffic:"按业务统计的发送／接收",receivePolls:"取消息与处理",bytesScope:"字节统计范围",acceptedBytes:"Steam 接受字节",rejectedBytes:"Steam 拒绝字节",attempts:"尝试次数",accepted:"接受次数",reliableFallbacks:"可靠频道快照回退次数",calls:"取消息次数",messages:"消息数",limitHits:"触及取消息上限次数",failedReads:"读取失败次数",maximumGapSeconds:"最大取消息间隔（秒）",totalProcessingSeconds:"累计处理时间（秒）",maximumProcessingSeconds:"最大处理时间（秒）",maxObservedAlive:"样本中的最大存活怪量",aliveSamples:"存活怪量样本",connectionInstance:"连接实例",direction:"网络方向",attempt:"发送尝试",channel:"频道",lane:"Steam 通道",messageNumber:"Steam 消息号",bytes:"字节",result:"Steam 返回码",injected:"模拟注入",routeEntity:"RPC 路由对象（非业务受影响对象）",entities:"真实业务对象",members:"批次成员",business:"业务类别",receiveSequence:"本端接收序号",complete:"解析完整",localIdentity:"本端 Steam 身份",remoteIdentity:"对端 Steam 身份",reliable:"可靠恢复",observed:"最近观测",unknown:"未知",invalid:"数据无效","not-applicable":"不适用","not-observed":"覆盖范围内未观察到",business:"业务等待",steam:"Steam 发送积压",frames:"本地帧停顿",writer:"日志写入积压",input:"输入",decision:"判断",change:"实际变化",build:"武器、装备与祝福",progression:"等级与经验",selection:"选卡状态",health:"生命",Health:"生命",MaxHealth:"生命上限",maximumHealth:"生命上限",alive:"存活",Alive:"存活",invulnerable:"无敌",position:"位置",level:"等级",Level:"等级",experience:"经验",Experience:"经验",weapons:"武器",Weapons:"武器",Equipment:"装备",equipment:"装备",Perks:"祝福",perks:"祝福",WeaponId:"武器",EquipmentId:"装备",PerkId:"祝福",SlotIndex:"槽位",LevelIndex:"等级索引",Rarity:"稀有度",buildRevision:"构筑版本",BuildRevision:"构筑版本",PendingUpgradeCount:"待选奖励",stateRevision:"本端状态版本",perspective:"观察视角",stage:"阶段",reason:"原因",outcome:"结果",source:"发起者",target:"目标",entity:"对象",dropId:"掉落物",claimVersion:"领取版本",amount:"数量",raw:"基础数量",awarded:"实际获得",restored:"恢复生命",before:"之前",after:"之后",state:"状态",pendingBytes:"待处理缓存",pendingReliable:"可靠发送积压（字节）",pendingUnreliable:"非可靠发送积压（字节）",queueMilliseconds:"发送等待（毫秒）",frameMaxMs:"最大帧耗时（毫秒）",capture:"来源端",sequence:"记录序号",elapsed:"本端经过秒数",rejected:"拒绝",connectionId:"本端连接",status:"状态",count:"数量",value:"数值",name:"名称",fields:"字段",baseline:"状态基准",continuous:"变化采集连续",eventId:"业务事件",optionId:"选项",options:"候选",offers:"候选",OptionId:"选项",ContentId:"内容",Kind:"类别",Stage:"选卡阶段",IsSelecting:"选卡锁定",isSelecting:"选卡锁定",selected:"选择",duration:"持续秒数",age:"已等待秒数"};
const explanations={BatchParseIncompleteObjectsMayBeUnknown:"批次解析不完整，未解析的业务对象保持未知。","Recorded evidence only; temporal overlap is not causation":"只显示已记录依据；同一时段出现不代表存在因果。","No recorded samples in selection":"此范围没有有效观测，不能据此排除问题。",LatestObservationOnly:"只有最近一次观测，无法确认此后是否变化。",CrossCaptureRevisionsAreNotComparable:"不同端的本地状态版本不能直接比较。",NoRecordedBeforeAfterState:"没有记录到该步骤的前后状态，不能推测实际变化。",DomainNotCapturedAtOrBeforeRequestedTime:"此时刻及之前未采集该类状态。",NoStateAtOrBeforeRequestedTime:"此时刻及之前没有可用状态。",UnknownEntityBirth:"对象出生实例不明确。",LocalEntityLifecycleEnded:"该本地对象生命周期已经结束。",RequestedTimeBeyondCapturedTail:"所选时刻超过该端已捕获的末尾。",EvidenceGapMayHideLaterMutation:"记录存在缺口，期间可能发生未保存的变化。",BaselineOrStateRevisionChainMissing:"缺少状态基准或连续版本，不能可靠恢复。",OperationInProgressOrCompletionMissing:"构筑操作尚未完成，或缺少完成记录。",DisappearanceOrResolutionDoesNotProveBenefit:"对象消失或请求结束，不能证明本地玩家获得收益。",ContentCatalogNotCaptured:"未保存与此包匹配的名称目录。"};
function explain(t){return explanations[t]||labels[t]||t;}
function fieldLabel(t){return String(t||"").split(".").map(x=>{const m=x.match(/^([^[]+)(.*)$/);return m?(labels[m[1]]||m[1])+m[2]:x;}).join(" / ");}
Object.assign(labels,{blessings:"祝福",locked:"选卡锁定",pendingUpgrades:"待选奖励",pendingEventId:"待处理选卡",definition:"构筑",active:"已启用",Snapshot:"快照",snapshot:"快照",CapturedAt:"采集时刻",ExperiencePerLevel:"升级所需经验",experiencePerLevel:"升级所需经验",StatusEffects:"状态效果",statusEffects:"状态效果",attackGate:"攻击门控",pendingDeaths:"待确认死亡数",oldestPendingDeathSeconds:"最久待确认年龄（秒）",evidenceQueuedBytes:"取证队列（字节）",logQueuedBytes:"日志队列（字节）",pendingReliableBytes:"可靠发送积压（字节）",pendingUnreliableBytes:"非可靠发送积压（字节）",unacknowledgedBytes:"待确认发送（字节）",health:"生命",actualAwarded:"实际获得经验",actualRestored:"本次实际回血",benefitConfirmed:"收益已确认",awaitingServerCommit:"等待服务端提交",accepted:"接受",valid:"有效",attribution:"归属限制",calculation:"计算过程",damage:"伤害",Damage:"伤害",Value:"请求数值",damageType:"伤害类型",requestedDamage:"计算得到的请求伤害",appliedDamage:"实际伤害",WeaponID:"武器 ID",PickupDropId:"回血掉落物",PickupClaimVersion:"领取版本",PickupRestoredHealth:"回执声明回血",Health:"生命",NetId:"网络对象",StateVersion:"状态版本",IsAlive:"存活",Server:"服务端",Owner:"所属玩家端",Replica:"远端观察",validSamples:"有有效指标的样本数",zeroOrBelowThresholdSamples:"零值或阈值内样本数",invalidSamples:"含无效指标的样本数",positiveSamples:"观察到等待/积压的样本数",positiveEvidence:"对应证据",MixedClockAgeUnavailable:"历史等待年龄混用时钟，不可用"});
Object.assign(labels,{Accepted:"接受",Rejected:"拒绝",Ignored:"忽略",Deferred:"延后",Applied:"已应用",Applying:"开始应用",Sent:"已发送",Received:"已接收",Produced:"已输出",Confirmed:"已确认",Requested:"已请求",Observed:"已观察",Committed:"已提交",Changed:"发生变化",Completed:"已完成",Failed:"失败",Ready:"可攻击",CoolingDown:"冷却中",OwnerBlocked:"玩家状态阻止攻击",Reward:"奖励选择",EquipmentTarget:"装备目标选择",ReceiptResubmitted:"已有回执重新提交",Coalesced:"多个变更合并输出",Direct:"直接输出",None:"无",Owner:"所属玩家端",Server:"服务端",Replica:"远端观察",sourcePlayerId:"发起玩家",SourcePlayerId:"发起玩家",SourceEntityId:"发起对象",TargetEntityId:"目标对象",TargetId:"目标对象",WeaponId:"武器 ID",WeaponLevel:"武器等级",AttackType:"攻击类型",upgradeLocked:"选卡锁定",loadingLocked:"载入锁定",cooldown:"冷却时长",gate:"攻击许可",weaponId:"武器 ID",stateVersion:"状态版本",localHealth:"本地生命",serverState:"服务端状态",replicaState:"远端已应用状态",canonicalAlive:"权威存活",localAlive:"本地存活",deathComplete:"死亡表现结束",attackObservation:"最近攻击观察",attackGate:"攻击许可",statusEffects:"状态效果",localState:"本地关键状态",LastObservedWeaponNotInventory:"只证明该次攻击使用此武器，不代表当前完整武器栏。",LatestAttackGateObservationOnly:"只显示最近一次攻击许可观测，不保证此后持续不变。"});
Object.assign(labels,{observation:"关键状态观测",attackAttributes:"最近攻击属性",attackPermission:"攻击许可",attackWeapon:"攻击时武器",attackStats:"攻击属性",AttackObservationDoesNotDescribeCurrentLoadout:"只证明最近记录的攻击情况，不能据此还原当前武器栏。",EntityId:"对象编号",OwnerPlayerId:"所属玩家",KillerPlayerId:"击杀玩家",AbsoluteInvulnerable:"绝对无敌",Authority:"权限编号",Enqueued:"已入队",BusinessBatchLinked:"记录中的业务批次候选关联",MixedClockAgeUnavailable:"历史时钟语义不一致，此年龄不可用。"});
Object.assign(labels,{candidate:"候选关联",confirmed:"已确认关联",context:"必要上下文","not-established":"关联不成立",ImportedFacetOnlyAssociation:"仅由旧索引的编号或消息哈希产生，尚未证实业务对应",UnverifiedTransportSequenceAssociation:"传输序号尚未验证业务对应",ConflictingTransportBusinessType:"记录中的业务类别冲突，不成立",evidence:"证据指针",coverageRef:"共享覆盖依据",gapCount:"已知缺口数"});
Object.assign(labels,{EventId:"业务事件编号",RootEventId:"根事件编号",ParentEventId:"父事件编号",CauseEventId:"原因事件编号",Sequence:"本端序号",PickupRound:"领取轮次",PlayerId:"玩家编号",actualTime:"游戏缩放时钟（秒）",bodyPosition:"物理位置观测",attackAge:"距上次攻击（秒）"});
Object.assign(labels,{dropped:"累计丢弃记录",peakQueueBytes:"采样记录的队列峰值（字节）",peakRetainedBytes:"采样记录的计费峰值（字节）",retainedBytes:"诊断计费内存（字节）",writerFailure:"写入失败",writerMs:"累计写入墙钟耗时（毫秒）",recordedFacts:"累计观测事实",semantics:"含义",CumulativeAtObservationNotSelectedIntervalDelta:"样本时已累计的数值，不能当作所选区间内新增量"});
function endpoint(c){return c.actualRole||(c.actualRoles||[]).join(" / ")||((c.roles||[]).includes("Host")?"Host":"端角色待核实");}
function birthLabel(generation){
 if(!generation||generation==="unknown")return "出生未知";
 return (generation.startsWith("avatar:")?"化身代次 ":"出生实例 ")+generation.split(":").pop();
}
let session={},matches=[],entities=[],selection={},cursor=null,selected=null,stateTarget=null,querySerial=0,exportPreview=null;
function node(tag,text,cls){const n=document.createElement(tag);if(text!==undefined&&text!==null)n.textContent=String(text);if(cls)n.className=cls;return n;}
function message(text,error=false){$("message").textContent=text;$("message").className=error?"error":"";}
function shown(v){if(v===null||v===undefined)return "未知";if(typeof v==="boolean")return v?"是":"否";return typeof v==="string"?(labels[v]||explanations[v]||v):String(v);}
async function api(path,data={},post=false){const r=await fetch("/api/"+path+(post?"":"?q="+encodeURIComponent(JSON.stringify(data))),post?{method:"POST",headers:{"Content-Type":"application/json","X-Investigation-Token":session.token},body:JSON.stringify(data)}:{});const result=await r.json();if(!r.ok)throw new Error(result.error||"请求失败");return result;}
function busy(button,fn){button.disabled=true;return Promise.resolve().then(fn).catch(e=>{message(e.message,true);if($("export-dialog")?.open){$("export-result").textContent=e.message;$("export-result").className="error";}}).finally(()=>button.disabled=false);}
function option(select,value,text){const n=node("option",text);n.value=value;select.append(n);}
function evidenceText(e){if(!e)return "来源未记录";return [e.capture||e.captureId,e.sequence||e.recordSequence].filter(Boolean).join(" / #");}
function readable(value,depth=0){
 if(value===null||value===undefined||typeof value!=="object")return node("span",shown(value));
 if(depth>5)return node("span","嵌套内容请展开原始证据");
 if(Array.isArray(value)){if(!value.length)return node("span","无");const list=node("div");value.forEach((v,i)=>{const row=node("div");if(typeof v==="object")row.append(node("small","项目 "+(i+1)));row.append(readable(v,depth+1));list.append(row)});return list;}
 const table=node("table");for(const [k,v]of Object.entries(value)){if(k==="references"||k==="raw"||k==="body")continue;const row=node("tr");row.append(node("td",k==="business"?"业务类别":fieldLabel(k)));const cell=node("td");cell.append(readable(v,depth+1));row.append(cell);table.append(row);}return table;
}
function rawDetails(title,value){
 const detail=node("details");detail.append(node("summary",title));let loaded=false;
 detail.addEventListener("toggle",()=>{if(detail.open&&!loaded){loaded=true;detail.append(node("pre",JSON.stringify(value,null,2)));}});
 return detail;
}
function readSelection(){const match=matches[Number($("match").value)];if(!match)throw new Error("归档内没有可调查的对局。");const s={run:match.run,round:match.round,captures:$("capture").value?[$("capture").value]:(match.captures||[]).map(x=>x.capture),direction:$("direction").value,category:$("category").value};for(const k of ["after","before"]){if($(k).value!==""){const n=Number($(k).value);if(!Number.isFinite(n)||n<0)throw new Error("时间范围必须为非负秒数。");s[k]=n;}}if(s.before!==undefined&&s.after!==undefined&&s.before<s.after)throw new Error("终点不能早于起点。");if($("entity").value!==""){const e=entities[Number($("entity").value)];s.entity=String(e.entity);if(e.generation)s.generation=e.generation;if(e.capture)s.entityCapture=e.capture;stateTarget={capture:e.capture,entity:String(e.entity)};}for(const [id,key] of [["network-direction","networkDirection"],["network-channel","channel"],["network-result","returnCode"]])if($(id).value)s[key]=$(id).value;if($("network-connection").value){const [cap,instance]=JSON.parse($("network-connection").value);s.connectionCapture=cap;s.connectionInstance=instance;}return s;}
async function matchChanged(){const m=matches[Number($("match").value)];$("network-connection").value="";if((m.captures||[]).every(c=>c.lightweightNetwork))$("category").value="network_rejection";$("capture").replaceChildren();option($("capture"),"","全部已导入端");for(const c of m.captures||[])option($("capture"),c.capture,endpoint(c)+" · "+c.capture.slice(0,8)+" · 包 "+(c.buildId||"未知"));await loadEntities();await query();}
async function loadEntities(){const m=matches[Number($("match").value)];if(!m)return;const s={run:m.run,round:m.round,captures:$("capture").value?[$("capture").value]:(m.captures||[]).map(x=>x.capture)};const result=await api("entities",{selection:s});entities=result.items||[];$("entity").replaceChildren();option($("entity"),"","全部对象");entities.forEach((e,i)=>option($("entity"),i,(e.name||e.kind||"对象")+" #"+e.entity+" · "+(e.capture||"").slice(0,6)+(" · "+birthLabel(e.generation))));stateTarget=null;const connections=session.networkDiagnostics?await api("connections",{selection:s}):{items:[]};$("network-connection").replaceChildren();option($("network-connection"),"","全部连接");const seen=new Set();for(const n of connections.items||[]){const key=JSON.stringify([n.capture,n.instance]);if(!seen.has(key)){seen.add(key);option($("network-connection"),key,n.capture.slice(0,8)+" · "+n.instance);}}}
function renderCoverage(c){const gaps=c.scopeGaps||[];const count=(c.intervals||[]).reduce((n,x)=>n+(x.gaps||[]).length,0)+gaps.length;$("coverage").textContent=(c.complete?"所选范围的已导入证据检查通过。":"所选范围存在缺口或完整性尚不能确认。")+" 这不证明已经收齐所有参与端。"+(count?" 已知缺口 "+count+" 处。":"");$("coverage").className=c.complete?"":"outside";$("sources").replaceChildren();for(const m of matches.filter(x=>x.run===selection.run&&x.round===selection.round))for(const cap of m.captures||[])$("sources").append(node("p",endpoint(cap)+" · "+cap.capture+" · 包 "+(cap.buildId||"未知")));$("sources").append(rawDetails("完整性检查的原始依据",c));}
function spark(parent,samples,track){
 const fields={writer:["evidenceQueuedBytes","logQueuedBytes","pendingBytes"],frames:["frameMaxMs"],business:["pendingDeaths"],steam:["pendingReliableBytes","pendingUnreliableBytes","queueMilliseconds"]}[track]||[];
 const groups=new Map();
 for(const sample of samples){
  if(!Number.isFinite(sample.elapsed))continue;
  const rows=track==="steam"?(sample.values?.connections||[]):[sample.values||{}];
  for(const row of rows){
   for(const field of fields){
    const value=row[field];
    if(typeof value!=="number"||!Number.isFinite(value)||value<0)continue;
    const connection=track==="steam"?String(row.connectionId??row.connection??"未记录连接编号"):"";
    const key=[sample.capture,connection,field].join("|");
    if(!groups.has(key))groups.set(key,{capture:sample.capture,connection,field,points:[]});
    groups.get(key).points.push({time:sample.elapsed,value});
   }
  }
 }
 for(const group of groups.values()){
  const points=group.points.sort((a,b)=>a.time-b.time),first=points[0],last=points[points.length-1];
  const peak=points.reduce((m,p)=>Math.max(m,p.value),0);
  const bytes=/Bytes$/.test(group.field);
  const peakText=bytes?(peak/1048576).toFixed(2)+" MiB":peak.toLocaleString("zh-CN",{maximumFractionDigits:2});
  parent.append(node("small",group.capture.slice(0,8)+(group.connection?" · 连接 "+group.connection:"")+" · "+fieldLabel(group.field)+" · 峰值 "+peakText,"hint"));
  if(points.length<2)continue;
  const svg=document.createElementNS("http://www.w3.org/2000/svg","svg");
  svg.setAttribute("viewBox","0 0 250 45");svg.setAttribute("role","img");
  svg.setAttribute("aria-label",fieldLabel(group.field)+"，来源 "+group.capture+"，独立本端采样时刻；空白不表示零值");
  for(const point of points){
   const circle=document.createElementNS(svg.namespaceURI,"circle");
   circle.setAttribute("cx",String(3+(point.time-first.time)/Math.max(last.time-first.time,0.001)*244));
   circle.setAttribute("cy",String(42-point.value/Math.max(peak,1)*39));
   circle.setAttribute("r","1.2");circle.setAttribute("fill","#73c6bb");svg.append(circle);
  }
  parent.append(svg,node("small",first.time.toFixed(1)+" — "+last.time.toFixed(1)+" 本端秒 · 离散样本","hint"));
 }
}
function renderTracks(result){$("tracks").replaceChildren();const collection=result.tracks||result;for(const key of ["business","steam","frames","writer"]){const t=Array.isArray(collection)?collection.find(x=>(x.id||x.kind||x.name)===key)||{}:collection[key]||{};const card=node("section",null,"track");card.append(node("h2",labels[key]));const status=t.status||"unknown";card.append(node("span",status==="observed"?"已观察到":labels[status]||status,"track-status "+status));const reasons=t.reasons||t.notes||[];card.append(node("p",explain(t.summary||t.description||t.reason||(Array.isArray(reasons)?reasons.join("；"):reasons)||"该范围缺少可用观测。")));if(key==="writer")for(const fact of t.metrics?.recordedFacts||[]){if(fact.name==="dropped")card.append(node("p",fact.capture.slice(0,8)+" · 样本中的累计丢弃最高值："+fact.value+" 条",fact.value>0?"outside":"hint"));}if(key==="steam"&&result.networkLoad){const load=node("details");load.append(node("summary","业务发送负载与接收处理"),node("p","传输总量与解码业务字节分别统计，不可相加。取消息触及上限不证明拥堵原因。","hint"),readable(result.networkLoad));card.append(load);}const chart=node("details");chart.append(node("summary","按端查看观测趋势"));let chartReady=false;chart.addEventListener("toggle",()=>{if(chart.open&&!chartReady){chartReady=true;spark(chart,t.samples||[],key);if(chart.children.length===1)chart.append(node("p","此范围没有可绘制的有效数值样本。","hint"));}});card.append(chart);const d=node("details");d.append(node("summary","样本、限制与依据"));if(t.metrics)d.append(readable(t.metrics));if(reasons.length)d.append(readable(reasons));for(const sample of (t.samples||[]).slice(0,12))d.append(readable(sample));if((t.samples||[]).length>12)d.append(node("p","页面仅展示前 12 个样本详情；轨道判断使用完整查询结果。"));d.append(rawDetails("完整轨道数据",t));card.append(d);$("tracks").append(card);}}
function addEvents(items){for(const item of items){const b=node("button",null,"event");const moment=node("span",(Number.isFinite(item.elapsed)?item.elapsed.toFixed(3)+" s":"时刻未知"),"moment");moment.append(node("small",(item.role||"来源")+" · "+item.capture.slice(0,8)));const text=node("span");text.append(node("span",item.summary||item.stage,"description"));text.append(node("small",(item.source?"发起 #"+item.source+"  ":"")+(item.target?"目标 #"+item.target+"  ":"")+"记录 #"+item.sequence));b.append(moment,text);b.onclick=()=>busy(b,async()=>{document.querySelectorAll(".event.selected").forEach(e=>e.classList.remove("selected"));b.classList.add("selected");await showDetail(item);});$("events").append(b);}}
async function query(){const serial=++querySerial;selection=readSelection();cursor=null;selected=null;$("detail").replaceChildren();$("detail-panel").querySelector(".empty").hidden=false;$("events").replaceChildren();message("正在查询完整归档…");const result=await api("timeline",{selection});if(serial!==querySerial)return;addEvents(result.items||[]);cursor=result.nextCursor;$("more").hidden=!cursor;$("count").textContent="符合条件共 "+(result.total??"未知")+" 条；当前展示 "+(result.items||[]).length+" 条";$("page-note").textContent=cursor?"继续加载可查看全部结果，筛选作用于整个归档。":"已展示此筛选的全部结果。";if(!(result.items||[]).length)$("events").append(node("p","没有找到符合条件的记录；未记录不表示未发生。","empty"));const [coverage,tracks,networkLoad]=await Promise.all([api("coverage",{selection}),api("tracks",{selection}),session.networkDiagnostics?api("network-load",{selection}):Promise.resolve(null)]);tracks.networkLoad=networkLoad;if(serial!==querySerial)return;renderCoverage(coverage);renderTracks(tracks);message("查询完成。点击事件展开调查过程。");}
async function showDetail(item){
 showInspector(false);
 selected={capture:item.capture,sequence:String(item.sequence)};
 const result=await api("detail",{selection,...selected});
 const root=$("detail");root.replaceChildren();
 $("detail-panel").querySelector(".empty").hidden=true;
 root.append(node("h3",item.summary||item.stage));
 if(result.summary)root.append(node("p",result.summary,"hint"));
 if(result.unknowns?.length)root.append(node("div",result.unknowns.map(x=>typeof x==="string"?explain(x):explain(x.reason||x.message||shown(x))).join("；"),"warning"));
 if(result.networkFacts){root.append(node("p","后续接受仅表示 Steam 接受；接收不等于业务应用。未自动推断恢复。","hint"),readable(result.networkFacts));}
 const steps=result.steps||[];
 for(const kind of ["input","decision","change"]){
  const group=steps.filter(step=>step.kind===kind);
  const section=node("section",null,"phase");
  section.append(node("h3",labels[kind]+" · "+group.length+" 个已记录阶段"));
  let opened=false;
  for(const step of group){
   const detail=node("details",null,"step");
   const title=(step.title||step.summary||step.stage||"").replace(/；事件 \d+/g,"");
   detail.append(node("summary",title));
   if(step.evidence?.capture===selected.capture&&String(step.evidence?.sequence)===selected.sequence){detail.open=true;opened=true;}
   if(step.outsideSelection)detail.append(node("span","范围外上下文","badge outside"));
   if(step.status==="unknown")detail.append(node("p",explain(step.reason||"NoRecordedBeforeAfterState"),"hint"));
   detail.append(readable(step.fields||step.values||step.description||{}));
   if(step.contentNames?.length){
    for(const field of step.contentNames)if(field.contentName?.name)detail.append(node("p",fieldLabel(field.name)+"："+field.contentName.name,"hint"));
   }
   if(step.evidence)detail.append(node("small",evidenceText(step.evidence),"hint"));
   section.append(detail);
  }
  if(!group.length)section.append(node("p","缺少此环节的记录，不能推测其结果。","empty"));
  root.append(section);
 }
 if(result.candidates?.length){
  const candidates=node("details",null,"warning");
  candidates.append(node("summary","关联候选与不成立关系（"+result.candidates.length+" 条）"));
  candidates.append(node("p","以下记录未进入已确认的操作过程；相同批次编号或消息哈希本身不足以证明业务对应。"));
  for(const candidate of result.candidates)candidates.append(readable(candidate));
  root.append(candidates);
 }
 const records=result.records||[],raw=node("details");
 raw.append(node("summary","原始证据与关联依据（"+records.length+" 条）"));
 for(const record of records){
  const title=(record.stage||"记录")+" · "+(record.captureId||record.capture||"").slice(0,8)+" #"+(record.recordSequence||record.sequence)+(record.outsideSelection?" · 范围外":"");
  raw.append(rawDetails(title,record));
 }
 if(result.associations)raw.append(readable(result.associations));
 root.append(raw);
 const entity=selection.entity||item.source||item.target;
 if(entity&&String(entity)!=="0"&&Number.isFinite(item.elapsed)){
  stateTarget={capture:item.capture,entity:String(entity)};
  $("state-time").value=item.elapsed.toFixed(6);await showState();
 }
}
async function showState(){
 if(!stateTarget)throw new Error("请先选择一个对象，或点击与对象相关的事件。");
 const at=Number($("state-time").value);
 if(!Number.isFinite(at)||at<0)throw new Error("请输入有效的状态时刻。");
 const result=await api("state",{selection,...stateTarget,at}),root=$("state");
 $("state-object").textContent="对象 #"+stateTarget.entity+" · "+stateTarget.capture.slice(0,8)+" · 本端 "+at.toFixed(3)+" 秒";
 root.replaceChildren();
 const order={build:0,progression:1,selection:2,observation:3,attackGate:4,attackAttributes:5};
 const domains=[...(result.domains||[]),...(result.missingDomains||[])].sort((a,b)=>(order[a.domain]??6)-(order[b.domain]??6));
 for(const domain of domains){
  const attributes=domain.domain==="attackAttributes";
  const section=node(attributes?"details":"section",null,"state-domain");
  const title=(domain.domain==="observation"&&domain.reason?"未单独采集的本地状态":labels[domain.domain]||domain.domain)+(domain.perspective?" · "+(labels[domain.perspective]||domain.perspective):"");
  section.append(node(attributes?"summary":"h3",title));
  if(attributes)section.append(node("p","这是最近一次攻击的属性，不代表当前完整武器栏。","hint"));
  const notes=[...(domain.unknowns||[])];if(domain.reason)notes.push(domain.reason);
  if(notes.length)section.append(node("p",[...new Set(notes)].map(explain).join("；"),"hint"));
  if(Number.isFinite(domain.observedAt))section.append(node("small","观测于本端 "+domain.observedAt.toFixed(3)+" 秒"+(Number.isFinite(domain.ageSeconds)?"，距所选时刻 "+domain.ageSeconds.toFixed(3)+" 秒":""),"hint"));
  if(domain.outsideSelection||domain.baseline?.outsideSelection)section.append(node("p","范围外上下文：为该时刻引入先前观测或状态基准。","outside"));
  const supplemental=node("details");supplemental.append(node("summary","其他已记录字段"));
  let extra=0;
  for(const field of domain.fields||[]){
   const box=node("div",null,"field"),status=field.status||"unknown";
   box.append(node("strong",domain.domain==="attackGate"&&field.name==="input.elapsed"?"距上次攻击（秒）":fieldLabel(field.name)),node("span",labels[status]||status,"badge "+status));
   const value=node("div",null,"value");
   if(status==="unknown"&&field.value!==undefined&&field.value!==null)value.append(node("small","已记录参考值；所选时刻仍未知。"));
   if(field.value!==undefined&&field.value!==null)value.append(readable(field.value));
   if(field.contentName?.name)value.append(node("span"," · "+field.contentName.name));
   box.append(value);
   if(domain.domain==="observation"&&!/(health|alive|invulnerable|status|position)/i.test(field.name)){supplemental.append(box);extra++;}
   else section.append(box);
  }
  if(extra)section.append(supplemental);
  if(domain.evidence)section.append(node("small",evidenceText(domain.evidence),"hint"));
  root.append(section);
 }
 if(!domains.length)root.append(node("p","没有可用状态基准；该时刻状态未知。","empty"));
 root.append(rawDetails("状态证据与恢复条件",result));
}
function showInspector(state){
 $("state-panel").hidden=!state;$("detail-panel").hidden=state;
 for(const [id,active]of [["view-process",!state],["view-state",state]]){
  $(id).classList.toggle("primary",active);$(id).setAttribute("aria-pressed",String(active));
 }
}
$("view-process").onclick=()=>showInspector(false);
$("view-state").onclick=()=>showInspector(true);
$("query").onclick=()=>busy($("query"),query);
$("match").onchange=()=>busy($("query"),matchChanged);
$("capture").onchange=()=>busy($("query"),async()=>{await loadEntities();await query();});
$("state-query").onclick=()=>busy($("state-query"),async()=>{selection=readSelection();await showState();});
$("more").onclick=()=>busy($("more"),async()=>{const serial=querySerial,result=await api("timeline",{selection,cursor});if(serial!==querySerial)return;addEvents(result.items||[]);cursor=result.nextCursor;$("more").hidden=!cursor;$("count").textContent="符合条件共 "+result.total+" 条；当前展示 "+$("events").children.length+" 条";$("page-note").textContent=cursor?"筛选作用于整个归档。":"已展示此筛选的全部结果。";});
$("export-open").onclick=()=>{$("export-scope").value=selected?"event":"range";$("export-preview").replaceChildren();$("confirm-export").hidden=true;$("export-result").textContent="";$("export-result").className="";exportPreview=null;$("export-dialog").showModal();};
function exportRequest(){if($("export-scope").value==="event"&&!selected)throw new Error("请先在时间线选择一个操作。");return {selection,records:$("export-scope").value==="event"?[selected]:[],description:$("description").value};}
function invalidatePreview(){exportPreview=null;$("confirm-export").hidden=true;}
$("description").oninput=invalidatePreview;$("export-scope").onchange=invalidatePreview;
$("preview-export").onclick=()=>busy($("preview-export"),async()=>{const p=await api("export/preview",exportRequest(),true);exportPreview=p;const r=$("export-preview");r.replaceChildren(node("p","选中 "+p.selectedRecords+" 条；必要上下文 "+p.contextRecords+" 条；原文件 "+p.files+" 个，原证据 "+((p.evidenceBytes??p.bytes)/1048576).toFixed(2)+" MiB。"));if(Number.isFinite(p.estimatedPackageBytes))r.append(node("p","报告与清单预计 "+(p.estimatedDerivedBytes/1048576).toFixed(2)+" MiB；问题包总量预计 "+(p.estimatedPackageBytes/1048576).toFixed(2)+" MiB。"));if(p.warnings?.length)r.append(node("div",p.warnings.join("；"),"warning"));r.append(node("p","复制完整原文件或压缩块可能附带其他记录；问题包会标明额外带入的范围。","hint"));$("confirm-export").hidden=false;});
$("confirm-export").onclick=()=>busy($("confirm-export"),async()=>{if(!exportPreview)throw new Error("请先计算必要上下文。");$("export-result").textContent="正在复制并校验必要证据…";const r=await api("export",{...exportRequest(),previewToken:exportPreview.previewToken},true);$("export-result").textContent="已保存："+r.directory+(r.complete?"":"（证据存在缺口，已在报告中保留）");$("confirm-export").hidden=true;});
(async()=>{try{session=await api("session");if(!session.networkDiagnostics){$("network-connection").closest("details").hidden=true;$("category").querySelector("option[value=network_rejection]").disabled=true;}$("database").textContent=session.database;const result=await api("matches");matches=(result.matches||[]).sort((a,b)=>a.round-b.round);matches.forEach((m,i)=>option($("match"),i,(m.label||m.run)+" / 第 "+m.round+" 轮 · "+(m.captures||[]).length+" 个来源"));if(matches.length)await matchChanged();else message("归档没有可调查的对局。请使用启动器选择完整导出目录。",true);}catch(e){message(e.message,true);}})();
