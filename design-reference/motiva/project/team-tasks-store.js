const KEY = 'motiva-team-tasks-v1';
const EVT = 'motiva-team-tasks-changed';

export const DEFAULT_TASKS = [
  { id: 't1', title: 'תיאום פגישת ייעוץ עם המנחה', type: 'team', desc: 'לתאם שעה ומקום מול המנחה לקראת סקירת אבן הדרך הבאה.', status: 'לביצוע', statusColor: '#8B8698', priority: 'רגילה', due: '31.07', assignees: ['נועה'], files: [], comments: 2, commentsHint: 'עמרי: "בואו נתאם ליום שלישי בבוקר"' },
  { id: 't2', title: 'עדכון מצגת הביניים', type: 'team', desc: 'לעדכן את השקפים 5–8 לפי המשוב האחרון מהמנחה.', status: 'הושלם', statusColor: '#0D9C9A', priority: 'נמוכה', due: '28.07', assignees: ['עמרי'], files: [{ name: 'מצגת_ביניים.pptx' }], comments: 4, commentsHint: 'נועה: "נראה מעולה, מוכן להגשה"' },
  { id: 't3', title: 'חלוקת אחריות למחקר', type: 'team', desc: 'לחלק בין חברי הצוות את פרקי הרקע התאורטי לכתיבה.', status: 'לביצוע', statusColor: '#8B8698', priority: 'גבוהה', due: '02.08', assignees: ['נועה', 'עמרי', 'דנה'], files: [], comments: 1, commentsHint: 'דנה: "אני לוקחת את פרק השיטות"' },
  { id: 't4', title: 'הכנת שאלות לראיון', type: 'team', desc: 'לנסח 10 שאלות לראיון המשתמש הבא, בהתאם למטרות המחקר.', status: 'בביצוע', statusColor: '#4F46E5', priority: 'גבוהה', due: '01.08', assignees: ['דנה'], files: [{ name: 'טיוטת_שאלות.docx' }], comments: 0, commentsHint: 'אין תגובות עדיין' },
  { id: 't5', title: 'סיכום פגישת הצוות האחרונה', type: 'team', desc: 'לכתוב ולשתף סיכום החלטות מפגישת הצוות מיום שני.', status: 'לביצוע', statusColor: '#8B8698', priority: 'רגילה', due: '30.07', assignees: ['נועה'], files: [], comments: 1, commentsHint: 'עמרי: "תוסיפי גם את ההחלטה על התקציב"' },
  { id: 't6', title: 'העלאת קבצים לתיקיית Drive המשותפת', type: 'team', desc: 'לוודא שכל הקבצים העדכניים מסונכרנים בתיקיית הצוות.', status: 'לביצוע', statusColor: '#8B8698', priority: 'נמוכה', due: '03.08', assignees: ['עמרי'], files: [], comments: 0, commentsHint: 'אין תגובות עדיין' },
  { id: 't7', title: 'בדיקת עיצוב נגד מסמך הדרישות', type: 'team', desc: 'להשוות בין מסך הדשבורד לבין דרישות המנחה מהמסמך המקורי.', status: 'בביצוע', statusColor: '#4F46E5', priority: 'רגילה', due: '04.08', assignees: ['דנה'], files: [], comments: 2, commentsHint: 'נועה: "שווה לבדוק גם את הצבעים"' },
  { id: 't8', title: 'תיאום ראיון משתמש נוסף', type: 'team', desc: 'לאתר משתתף נוסף ולתאם שעה לראיון השבוע.', status: 'לביצוע', statusColor: '#8B8698', priority: 'רגילה', due: '05.08', assignees: ['נועה', 'עמרי'], files: [], comments: 1, commentsHint: 'עמרי: "מכיר מישהי שיכולה להתאים"' }
];

export function loadTasks() {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return JSON.parse(raw);
  } catch (e) {}
  const seed = DEFAULT_TASKS.map(t => ({ ...t }));
  try { localStorage.setItem(KEY, JSON.stringify(seed)); } catch (e) {}
  return seed;
}

export function saveTasks(tasks) {
  try { localStorage.setItem(KEY, JSON.stringify(tasks)); } catch (e) {}
  try { window.dispatchEvent(new CustomEvent(EVT, { detail: tasks })); } catch (e) {}
}

export function subscribe(cb) {
  const onCustom = (e) => cb(e.detail);
  const onStorage = (e) => { if (e.key === KEY) cb(loadTasks()); };
  window.addEventListener(EVT, onCustom);
  window.addEventListener('storage', onStorage);
  return () => {
    window.removeEventListener(EVT, onCustom);
    window.removeEventListener('storage', onStorage);
  };
}
