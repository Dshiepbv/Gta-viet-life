/**
 * GTA Text - may chu trung gian chay tren Google (Apps Script), du lieu nam trong tai khoan Google cua Sep.
 * PC (dashboard.py) day so lieu len va keo lenh ve; dien thoai (APK) doc so lieu va gui lenh.
 * Khi PC tat, so lieu cuoi cung van o day, APK tu uoc tinh tiep; lenh xep hang cho den khi PC bat.
 *
 * SEP DOI HAI DONG DUOI (tu dat), roi Deploy > New deployment > Web app:
 *   Execute as: Me   |   Who has access: Anyone
 */
var PIN = 'DOI-MA-PIN-DIEN-THOAI';   // ma Sep nhap tren dien thoai
var KEY = 'DOI-KHOA-DUNG-CHO-PC';    // khoa rieng cho PC (dai, kho doan)

function out(o) { return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON); }

function doGet(e) {
  var p = e.parameter || {};
  var props = PropertiesService.getScriptProperties();
  if (p.a === 'pull') {                       // PC keo lenh ve
    if (p.key !== KEY) return out({ ok: false, msg: 'bad key' });
    var lock = LockService.getScriptLock(); lock.waitLock(5000);
    try {
      var q = JSON.parse(props.getProperty('cmds') || '[]');
      props.setProperty('cmds', '[]');
      return out({ ok: true, cmds: q });
    } finally { lock.releaseLock(); }
  }
  // dien thoai doc so lieu
  if (p.pin !== PIN) return out({ ok: false, error: 'Sai ma PIN', needPin: true });
  var snap = props.getProperty('snap');
  if (!snap) return out({ ok: true, error: 'chua co du lieu - bat PC va vao game mot lan', needPin: true });
  var d = JSON.parse(snap);
  d.pending = JSON.parse(props.getProperty('cmds') || '[]').map(function (l) { return l.split('|'); });
  d.needPin = true;
  return out(d);
}

function doPost(e) {
  var props = PropertiesService.getScriptProperties();
  var c;
  try { c = JSON.parse(e.postData.contents); } catch (x) { return out({ ok: false, msg: 'bad json' }); }
  if (c.a === 'push') {                       // PC day snapshot len
    if (c.key !== KEY) return out({ ok: false, msg: 'bad key' });
    var s = JSON.stringify(c.snap);
    if (s.length > 8500) return out({ ok: false, msg: 'snapshot qua lon' });
    props.setProperty('snap', s);
    return out({ ok: true });
  }
  if (c.a === 'cmd') {                        // dien thoai gui lenh
    if (c.pin !== PIN) return out({ ok: false, msg: 'Sai ma PIN' });
    var OPS = { restock: 1, hire: 1, fire: 1, upgrade: 1, collect: 1 };
    var idx = parseInt(c.idx, 10), arg = parseInt(c.arg || 0, 10);
    if (!OPS[c.op] || !(idx >= 0 && idx <= 20)) return out({ ok: false, msg: 'Lenh khong hop le' });
    var lock = LockService.getScriptLock(); lock.waitLock(5000);
    try {
      var q = JSON.parse(props.getProperty('cmds') || '[]');
      if (q.length >= 30) return out({ ok: false, msg: 'Hang doi day (30 lenh)' });
      q.push(new Date().getTime() + '|' + c.op + '|' + idx + '|' + arg);
      props.setProperty('cmds', JSON.stringify(q));
    } finally { lock.releaseLock(); }
    return out({ ok: true });
  }
  return out({ ok: false, msg: 'unknown' });
}
