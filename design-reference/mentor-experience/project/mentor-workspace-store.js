const KEY = 'motiva-mentor-workspace-v2';
const EVT = 'motiva-mentor-workspace-changed';

export const DEFAULT_DATA = {
  reviews: [
    { id: 'r1', projectId: 'p1', project: 'ניווט לעיוורים', team: 'צוות רועי כהן', milestone: 'אב־טיפוס', due: '30.07', receivedDaysAgo: 5, status: 'pending' },
    { id: 'r2', projectId: 'p2', project: 'מסמכי תיעוד למערכת', team: 'צוות איתי שם־טוב', milestone: 'תיעוד טכני', due: '02.08', receivedDaysAgo: 2, status: 'pending' },
    { id: 'r3', projectId: 'p3', project: 'אפליקציית ניהול זמן', team: 'צוות מאיה גל', milestone: 'עיצוב UI', due: '05.08', receivedDaysAgo: 1, status: 'pending' }
  ],
  requests: [
    { id: 'q1', projectId: 'p1', type: 'הארכת מועד', project: 'ניווט לעיוורים', people: 'רועי כהן, שני מור', summary: 'בקשה להארכת מועד הגשת אב־טיפוס', status: 'awaiting_mentor', waitingSince: 2 },
    { id: 'q2', projectId: 'p4', type: 'אתגר תוכן', project: 'מערכת המלצות תוכן', people: 'נועה לוי', summary: 'בקשה לשינוי היקף — מקור הנתונים שסוכם לא זמין', status: 'awaiting_mentor', waitingSince: 1 },
    { id: 'q3', projectId: 'p3', type: 'נסיבות מיוחדות', project: 'אפליקציית ניהול זמן', people: 'מאיה גל, תום אשר', summary: 'בקשה לפריסה מחדש של לוח הזמנים עקב מילואים', status: 'awaiting_mentor', waitingSince: 0 },
    { id: 'q4', projectId: 'p2', type: 'אתגר מול לקוח', project: 'מסמכי תיעוד למערכת', people: 'איתי שם־טוב', summary: 'הלקוח מסרב לחתום על מסמך הדרישות הסופי', status: 'with_lecturer', waitingSince: 3 },
    { id: 'q5', projectId: 'p3', type: 'הארכת מועד', project: 'אפליקציית ניהול זמן', people: 'מאיה גל', summary: 'נדרש פירוט נוסף לגבי משך ההארכה המבוקש', status: 'awaiting_team', waitingSince: 0 },
    { id: 'q6', projectId: 'p1', type: 'אחר', project: 'ניווט לעיוורים', people: 'רועי כהן, שני מור', summary: 'בקשה להחלפת חבר צוות — נדרשת הבהרה מהצוות', status: 'awaiting_team', waitingSince: 1 },
    { id: 'q7', projectId: 'p5', type: 'הארכת מועד', project: 'פרויקט הדרכה', people: 'דן פרץ', summary: 'בקשה להארכת מועד אושרה', status: 'closed', waitingSince: 0 },
    { id: 'q8', projectId: 'p6', type: 'אתגר תוכן', project: 'מיפוי נגישות', people: 'שירה בר', summary: 'בקשה לשינוי היקף נדחתה', status: 'closed', waitingSince: 0 }
  ],
  projects: [
    {
      id: 'p1', name: 'ניווט לעיוורים', team: 'רועי כהן, שני מור', milestone: 'אב־טיפוס', owner: 'מנחה', status: 'at_risk',
      lastActivity: 'לפני 5 ימים', nextAction: 'בקש עדכון מהסטודנט', pendingReviews: 1, pendingRequests: 2, deadline: '30.07',
      blocked: true, blockedReason: 'לא התקבל קובץ המקור של האב־טיפוס מהסטודנט — אין דרך להעריך את אבן הדרך בלעדיו.', blockedDays: 5,
      milestones: ['הצעת מחקר', 'דרישות', 'אב־טיפוס', 'מסירה סופית'], currentMilestoneIndex: 2,
      checklist: [
        { id: 'c1', label: 'בדיקת ההגשה', done: true },
        { id: 'c2', label: 'מתן משוב', done: false },
        { id: 'c3', label: 'קביעת פגישה', done: false },
        { id: 'c4', label: 'אישור אבן הדרך', done: false }
      ],
      interactions: [
        { id: 'i1', type: 'feedback', label: 'נשלח משוב על אבן הדרך "דרישות"', time: 'לפני 12 יום' },
        { id: 'i2', type: 'reply', label: 'רועי כהן הגיב לבקשת עדכון', time: 'לפני שבוע' },
        { id: 'i3', type: 'system', label: 'אבן הדרך "הצעת מחקר" אושרה', time: 'לפני 30 יום' }
      ]
    },
    {
      id: 'p2', name: 'מסמכי תיעוד למערכת', team: 'איתי שם־טוב', milestone: 'תיעוד טכני', owner: 'מנחה', status: 'attention',
      lastActivity: 'לפני יום', nextAction: 'בדוק הגשה', pendingReviews: 1, pendingRequests: 1, deadline: '02.08', blocked: false, blockedDays: 0,
      milestones: ['הצעת מחקר', 'דרישות', 'תיעוד טכני', 'מסירה סופית'], currentMilestoneIndex: 2,
      checklist: [{ id: 'c1', label: 'בדיקת ההגשה', done: false }, { id: 'c2', label: 'מתן משוב', done: false }],
      interactions: [{ id: 'i1', type: 'system', label: 'הועברה בקשה למרצה', time: 'לפני 3 ימים' }]
    },
    {
      id: 'p3', name: 'אפליקציית ניהול זמן', team: 'מאיה גל, תום אשר', milestone: 'עיצוב UI', owner: 'סטודנט', status: 'on_track',
      lastActivity: 'לפני 3 שעות', nextAction: 'בדוק הגשה', pendingReviews: 1, pendingRequests: 1, deadline: '05.08', blocked: false, blockedDays: 0,
      milestones: ['הצעת מחקר', 'דרישות', 'עיצוב UI', 'מסירה סופית'], currentMilestoneIndex: 2,
      checklist: [{ id: 'c1', label: 'בדיקת ההגשה', done: false }],
      interactions: [{ id: 'i1', type: 'reply', label: 'מאיה גל הגיבה בבקשה', time: 'לפני 3 שעות' }]
    },
    {
      id: 'p4', name: 'מערכת המלצות תוכן', team: 'נועה לוי', milestone: 'מסירה סופית', owner: 'סטודנט', status: 'on_track',
      lastActivity: 'אתמול', nextAction: 'אשר בקשה', pendingReviews: 0, pendingRequests: 1, deadline: '12.08', blocked: false, blockedDays: 0,
      milestones: ['הצעת מחקר', 'דרישות', 'אב־טיפוס', 'מסירה סופית'], currentMilestoneIndex: 3,
      checklist: [], interactions: [{ id: 'i1', type: 'system', label: 'נועה לוי שלחה בקשה', time: 'אתמול' }]
    }
  ]
};

export function loadWorkspace() {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return JSON.parse(raw);
  } catch (e) {}
  const seed = JSON.parse(JSON.stringify(DEFAULT_DATA));
  try { localStorage.setItem(KEY, JSON.stringify(seed)); } catch (e) {}
  return seed;
}

export function saveWorkspace(data) {
  try { localStorage.setItem(KEY, JSON.stringify(data)); } catch (e) {}
  try { window.dispatchEvent(new CustomEvent(EVT, { detail: data })); } catch (e) {}
}

export function subscribe(cb) {
  const onCustom = (e) => cb(e.detail);
  const onStorage = (e) => { if (e.key === KEY) cb(loadWorkspace()); };
  window.addEventListener(EVT, onCustom);
  window.addEventListener('storage', onStorage);
  return () => {
    window.removeEventListener(EVT, onCustom);
    window.removeEventListener('storage', onStorage);
  };
}

export function setReviewStatus(id, status) {
  const data = loadWorkspace();
  data.reviews = data.reviews.map(r => r.id === id ? { ...r, status } : r);
  saveWorkspace(data);
  return data;
}

export function setRequestStatus(id, status) {
  const data = loadWorkspace();
  data.requests = data.requests.map(r => r.id === id ? { ...r, status } : r);
  saveWorkspace(data);
  return data;
}

export function toggleChecklistItem(projectId, itemId) {
  const data = loadWorkspace();
  data.projects = data.projects.map(p => p.id === projectId
    ? { ...p, checklist: (p.checklist || []).map(c => c.id === itemId ? { ...c, done: !c.done } : c) }
    : p);
  saveWorkspace(data);
  return data;
}
