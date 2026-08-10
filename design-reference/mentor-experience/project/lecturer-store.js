/* Motiva — lecturer (course-level) data store.
   Same persistence + subscribe pattern as mentor-workspace-store.js. */

const KEY = 'motiva-lecturer-cohort-v2';
const EVT = 'motiva-lecturer-cohort-changed';

export const DEFAULT_DATA = {
  term: { label: 'סמסטר קיץ תשפ״ו', milestone: 'אב־טיפוס', week: 3, totalWeeks: 5 },
  projects: [
    { id: 'p1', name: 'ניווט לעיוורים', team: 'רועי כהן, שני מור', mentor: 'דנה כרמי', milestone: 'אב־טיפוס', status: 'at_risk',
      deadline: '30.07', lastActivity: 'לפני 5 ימים', openSubmissions: 1, openRequests: 2, missedDeadlines: 2,
      attention: 'שני מועדים הוחמצו · קובץ האב־טיפוס לא הוגש', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p2', name: 'מסמכי תיעוד למערכת', team: 'איתי שם־טוב', mentor: 'דנה כרמי', milestone: 'תיעוד טכני', status: 'attention',
      deadline: '02.08', lastActivity: 'לפני יום', openSubmissions: 1, openRequests: 1, missedDeadlines: 0,
      attention: 'בקשה הועברה להחלטת מרצה — הלקוח מסרב לחתום', milestones: ['הצעת מחקר','דרישות','תיעוד טכני','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p3', name: 'אפליקציית ניהול זמן', team: 'מאיה גל, תום אשר', mentor: 'דנה כרמי', milestone: 'עיצוב UI', status: 'on_track',
      deadline: '05.08', lastActivity: 'לפני 3 שעות', openSubmissions: 1, openRequests: 1, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','עיצוב UI','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p4', name: 'מערכת המלצות תוכן', team: 'נועה לוי', mentor: 'דנה כרמי', milestone: 'מסירה סופית', status: 'on_track',
      deadline: '12.08', lastActivity: 'אתמול', openSubmissions: 0, openRequests: 1, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 3 },
    { id: 'p5', name: 'Gradify Platform', team: 'נועה טננבאום, איתי לוי, רוני בר־און', mentor: 'אורי שגב', milestone: 'אב־טיפוס', status: 'on_track',
      deadline: '09.08', lastActivity: 'לפני 6 שעות', openSubmissions: 1, openRequests: 0, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p6', name: 'מיפוי נגישות עירונית', team: 'שירה בר, עומר דגן', mentor: 'אורי שגב', milestone: 'אב־טיפוס', status: 'attention',
      deadline: '08.08', lastActivity: 'לפני 4 ימים', openSubmissions: 2, openRequests: 0, missedDeadlines: 1,
      attention: 'שתי הגשות באיחור באותה אבן דרך', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p7', name: 'ניתוח רגשות בטקסט', team: 'יובל אדרי', mentor: 'אורי שגב', milestone: 'דרישות', status: 'at_risk',
      deadline: '01.08', lastActivity: 'לפני 11 ימים', openSubmissions: 2, openRequests: 1, missedDeadlines: 3,
      attention: 'אין פעילות 11 ימים · הצוות לא נענה לפניות המנחה', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 1 },
    { id: 'p8', name: 'מערכת שיבוץ מתנדבים', team: 'תמר קדם, יונתן ספיר', mentor: 'רות מזרחי', milestone: 'אב־טיפוס', status: 'on_track',
      deadline: '10.08', lastActivity: 'אתמול', openSubmissions: 1, openRequests: 0, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p9', name: 'זיהוי תקלות בייצור', team: 'אלון ברק', mentor: 'רות מזרחי', milestone: 'אב־טיפוס', status: 'attention',
      deadline: '07.08', lastActivity: 'לפני 3 ימים', openSubmissions: 1, openRequests: 1, missedDeadlines: 1,
      attention: 'בקשה להחלפת נושא ממתינה להחלטת מרצה', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p10', name: 'פלטפורמת לימוד עמיתים', team: 'הילה נחום, דניאל אבו', mentor: 'רות מזרחי', milestone: 'עיצוב UI', status: 'on_track',
      deadline: '11.08', lastActivity: 'לפני 2 ימים', openSubmissions: 0, openRequests: 0, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','עיצוב UI','מסירה סופית'], milestoneIndex: 2 },
    { id: 'p11', name: 'ארכיון קול קהילתי', team: 'ליאור פיאמנטה', mentor: 'עמית רוזן', milestone: 'דרישות', status: 'on_track',
      deadline: '13.08', lastActivity: 'לפני 5 שעות', openSubmissions: 1, openRequests: 0, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 1 },
    { id: 'p12', name: 'סימולטור תנועה עירונית', team: 'גיא אלמוג, מיכל שריד', mentor: 'עמית רוזן', milestone: 'אב־טיפוס', status: 'on_track',
      deadline: '09.08', lastActivity: 'אתמול', openSubmissions: 1, openRequests: 0, missedDeadlines: 0,
      attention: '', milestones: ['הצעת מחקר','דרישות','אב־טיפוס','מסירה סופית'], milestoneIndex: 2 }
  ],
  submissions: [
    { id: 's1', projectId: 'p1', project: 'ניווט לעיוורים', mentor: 'דנה כרמי', milestone: 'אב־טיפוס', type: 'קובץ אב־טיפוס', due: '30.07', status: 'missing', daysLate: 11 },
    { id: 's2', projectId: 'p7', project: 'ניתוח רגשות בטקסט', mentor: 'אורי שגב', milestone: 'דרישות', type: 'מסמך דרישות', due: '01.08', status: 'missing', daysLate: 9 },
    { id: 's3', projectId: 'p7', project: 'ניתוח רגשות בטקסט', mentor: 'אורי שגב', milestone: 'דרישות', type: 'תרשים ארכיטקטורה', due: '04.08', status: 'late', daysLate: 6 },
    { id: 's4', projectId: 'p6', project: 'מיפוי נגישות עירונית', mentor: 'אורי שגב', milestone: 'אב־טיפוס', type: 'סרטון הדגמה', due: '05.08', status: 'late', daysLate: 5 },
    { id: 's5', projectId: 'p6', project: 'מיפוי נגישות עירונית', mentor: 'אורי שגב', milestone: 'אב־טיפוס', type: 'קובץ אב־טיפוס', due: '08.08', status: 'awaiting_mentor', daysLate: 0 },
    { id: 's6', projectId: 'p2', project: 'מסמכי תיעוד למערכת', mentor: 'דנה כרמי', milestone: 'תיעוד טכני', type: 'מסמך תיעוד', due: '02.08', status: 'awaiting_lecturer', daysLate: 3 },
    { id: 's7', projectId: 'p9', project: 'זיהוי תקלות בייצור', mentor: 'רות מזרחי', milestone: 'אב־טיפוס', type: 'קובץ אב־טיפוס', due: '07.08', status: 'awaiting_lecturer', daysLate: 1 },
    { id: 's8', projectId: 'p3', project: 'אפליקציית ניהול זמן', mentor: 'דנה כרמי', milestone: 'עיצוב UI', type: 'מסמך עיצוב', due: '05.08', status: 'awaiting_mentor', daysLate: 0 },
    { id: 's9', projectId: 'p5', project: 'Gradify Platform', mentor: 'אורי שגב', milestone: 'אב־טיפוס', type: 'קובץ אב־טיפוס', due: '09.08', status: 'awaiting_mentor', daysLate: 0 },
    { id: 's10', projectId: 'p8', project: 'מערכת שיבוץ מתנדבים', mentor: 'רות מזרחי', milestone: 'אב־טיפוס', type: 'קובץ אב־טיפוס', due: '10.08', status: 'awaiting_mentor', daysLate: 0 },
    { id: 's11', projectId: 'p11', project: 'ארכיון קול קהילתי', mentor: 'עמית רוזן', milestone: 'דרישות', type: 'מסמך דרישות', due: '13.08', status: 'awaiting_mentor', daysLate: 0 },
    { id: 's12', projectId: 'p12', project: 'סימולטור תנועה עירונית', mentor: 'עמית רוזן', milestone: 'אב־טיפוס', type: 'קובץ אב־טיפוס', due: '09.08', status: 'approved', daysLate: 0 },
    { id: 's13', projectId: 'p10', project: 'פלטפורמת לימוד עמיתים', mentor: 'רות מזרחי', milestone: 'עיצוב UI', type: 'מסמך עיצוב', due: '04.08', status: 'approved', daysLate: 0 },
    { id: 's14', projectId: 'p4', project: 'מערכת המלצות תוכן', mentor: 'דנה כרמי', milestone: 'מסירה סופית', type: 'דוח סופי', due: '12.08', status: 'awaiting_mentor', daysLate: 0 }
  ],
  requests: [
    { id: 'q1', projectId: 'p2', project: 'מסמכי תיעוד למערכת', team: 'איתי שם־טוב', mentor: 'דנה כרמי', type: 'אתגר מול לקוח',
      summary: 'הלקוח מסרב לחתום על מסמך הדרישות הסופי', status: 'awaiting_lecturer', waitingDays: 3,
      escalation: 'המנחה העבירה להחלטת מרצה — נדרש אישור לשינוי תנאי ההגשה',
      decision: 'אישור הגשה ללא חתימת לקוח או דחיית אבן הדרך',
      history: [
        { time: 'לפני 6 ימים', text: 'הבקשה נפתחה על ידי איתי שם־טוב' },
        { time: 'לפני 4 ימים', text: 'דנה כרמי ביקשה אסמכתאות מהצוות' },
        { time: 'לפני 3 ימים', text: 'דנה כרמי העבירה את הבקשה להחלטת מרצה' }
      ] },
    { id: 'q2', projectId: 'p9', project: 'זיהוי תקלות בייצור', team: 'אלון ברק', mentor: 'רות מזרחי', type: 'שינוי נושא',
      summary: 'בקשה להחלפת נושא הפרויקט לאחר שהנתונים מהמפעל לא אושרו', status: 'awaiting_lecturer', waitingDays: 5,
      escalation: 'שינוי נושא מחייב אישור מרצה בהתאם לנוהל הקורס',
      decision: 'אישור נושא חלופי או המשך בנושא המקורי עם היקף מצומצם',
      history: [
        { time: 'לפני 9 ימים', text: 'הבקשה נפתחה על ידי אלון ברק' },
        { time: 'לפני 6 ימים', text: 'רות מזרחי אישרה את הצורך בשינוי' },
        { time: 'לפני 5 ימים', text: 'הבקשה הועברה להחלטת מרצה' }
      ] },
    { id: 'q3', projectId: 'p7', project: 'ניתוח רגשות בטקסט', team: 'יובל אדרי', mentor: 'אורי שגב', type: 'נסיבות מיוחדות',
      summary: 'בקשה לפריסה מחדש של לוח הזמנים בשל היעדרות ממושכת', status: 'awaiting_lecturer', waitingDays: 2,
      escalation: 'חריגה מלוח הזמנים של הקורס — נדרשת החלטה אקדמית',
      decision: 'אישור לוח זמנים חלופי או העברה לסמסטר הבא',
      history: [
        { time: 'לפני 5 ימים', text: 'הבקשה נפתחה על ידי יובל אדרי' },
        { time: 'לפני 2 ימים', text: 'אורי שגב העביר את הבקשה להחלטת מרצה' }
      ] },
    { id: 'q4', projectId: 'p1', project: 'ניווט לעיוורים', team: 'רועי כהן, שני מור', mentor: 'דנה כרמי', type: 'הארכת מועד',
      summary: 'בקשה להארכת מועד הגשת אב־טיפוס בשבוע', status: 'awaiting_mentor', waitingDays: 2,
      escalation: '', decision: '', history: [{ time: 'לפני 2 ימים', text: 'הבקשה נפתחה על ידי רועי כהן' }] },
    { id: 'q5', projectId: 'p6', project: 'מיפוי נגישות עירונית', team: 'שירה בר', mentor: 'אורי שגב', type: 'אתגר תוכן',
      summary: 'מקור הנתונים העירוני שסוכם אינו זמין', status: 'awaiting_team', waitingDays: 1,
      escalation: '', decision: '', history: [{ time: 'לפני 4 ימים', text: 'המנחה ביקש פירוט נוסף מהצוות' }] },
    { id: 'q6', projectId: 'p4', project: 'מערכת המלצות תוכן', team: 'נועה לוי', mentor: 'דנה כרמי', type: 'הארכת מועד',
      summary: 'הארכת מועד להגשת הדוח הסופי אושרה בשלושה ימים', status: 'resolved', waitingDays: 0,
      escalation: '', decision: '', history: [{ time: 'לפני 8 ימים', text: 'הבקשה אושרה על ידי המרצה' }] },
    { id: 'q7', projectId: 'p3', project: 'אפליקציית ניהול זמן', team: 'מאיה גל', mentor: 'דנה כרמי', type: 'נסיבות מיוחדות',
      summary: 'פריסה מחדש של לוח הזמנים עקב מילואים אושרה', status: 'resolved', waitingDays: 0,
      escalation: '', decision: '', history: [{ time: 'לפני 12 יום', text: 'הבקשה אושרה על ידי המרצה' }] }
  ],
  events: [
    { id: 'e1', day: 10, type: 'review', title: 'סיום חלון בדיקת אב־טיפוס', context: 'כל הפרויקטים · מנחים' },
    { id: 'e2', day: 11, type: 'meeting', title: 'ישיבת סגל פרויקטי גמר', context: '4 מנחים · חדר 305' },
    { id: 'e3', day: 12, type: 'milestone', title: 'מועד אבן הדרך: אב־טיפוס', context: '8 פרויקטים' },
    { id: 'e4', day: 13, type: 'submission', title: 'פתיחת חלון הגשת תיעוד טכני', context: 'שנה ג׳ · כל הפרויקטים' },
    { id: 'e5', day: 17, type: 'meeting', title: 'שיחות סטטוס עם מנחים', context: 'דנה כרמי, אורי שגב' },
    { id: 'e6', day: 19, type: 'review', title: 'תקופת בדיקה: תיעוד טכני', context: 'עד 24.08' },
    { id: 'e7', day: 20, type: 'lecturer', title: 'החלטות ממתינות: בקשות מועברות', context: '3 בקשות · יעד פנימי' },
    { id: 'e8', day: 24, type: 'submission', title: 'סגירת חלון הגשת תיעוד טכני', context: 'כל הפרויקטים' },
    { id: 'e9', day: 26, type: 'presentation', title: 'מצגות ביניים', context: '12 פרויקטים · אולם ב׳' },
    { id: 'e10', day: 31, type: 'milestone', title: 'מועד אבן הדרך: מסירה סופית', context: '2 פרויקטים' }
  ],
  activity: [
    { id: 'a1', kind: 'escalation', text: 'רות מזרחי העבירה בקשת שינוי נושא להחלטתך', context: 'זיהוי תקלות בייצור', time: 'לפני 5 שעות' },
    { id: 'a2', kind: 'status', text: 'הפרויקט "ניתוח רגשות בטקסט" סומן בסיכון', context: 'אין פעילות 11 ימים', time: 'אתמול' },
    { id: 'a3', kind: 'decision', text: 'החלטתך על הארכת מועד יושמה בלוח הזמנים', context: 'מערכת המלצות תוכן', time: 'לפני 2 ימים' },
    { id: 'a4', kind: 'submission', text: 'הגשת "מסמך תיעוד" הועברה לאישור מרצה', context: 'מסמכי תיעוד למערכת', time: 'לפני 3 ימים' }
  ]
};

export function loadCohort() {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return JSON.parse(raw);
  } catch (e) {}
  const seed = JSON.parse(JSON.stringify(DEFAULT_DATA));
  try { localStorage.setItem(KEY, JSON.stringify(seed)); } catch (e) {}
  return seed;
}

export function saveCohort(data) {
  try { localStorage.setItem(KEY, JSON.stringify(data)); } catch (e) {}
  try { window.dispatchEvent(new CustomEvent(EVT, { detail: data })); } catch (e) {}
}

export function subscribe(cb) {
  const onCustom = (e) => cb(e.detail);
  const onStorage = (e) => { if (e.key === KEY) cb(loadCohort()); };
  window.addEventListener(EVT, onCustom);
  window.addEventListener('storage', onStorage);
  return () => {
    window.removeEventListener(EVT, onCustom);
    window.removeEventListener('storage', onStorage);
  };
}

export function setRequestStatus(id, status) {
  const data = loadCohort();
  data.requests = data.requests.map(r => r.id === id ? { ...r, status, waitingDays: 0 } : r);
  saveCohort(data);
  return data;
}

export function setSubmissionStatus(id, status) {
  const data = loadCohort();
  data.submissions = data.submissions.map(s => s.id === id ? { ...s, status } : s);
  saveCohort(data);
  return data;
}
