const KEY = 'motiva-mentor-personal-tasks-v2';
const EVT = 'motiva-mentor-tasks-changed';

export const DEFAULT_TASKS = [
  { id: 'm1', title: 'שליחת סיכום שבועי לצוות', status: 'לביצוע', due: '31.07' },
  { id: 'm2', title: 'הכנה לפגישה הבאה עם רועי כהן', status: 'בביצוע', due: '01.08' },
  { id: 'm3', title: 'העלאת גיליון הערכה למערכת', status: 'לביצוע', due: '02.08' },
  { id: 'm4', title: 'בדיקת מצגת ביניים — צוות מאיה גל', status: 'הושלם', due: '28.07' },
  { id: 'm5', title: 'יצירת קשר עם בודק חיצוני', status: 'לביצוע', due: '05.08' },
  { id: 'm6', title: 'סיכום פגישת ייעוץ עם ראש התוכנית', status: 'לביצוע', due: '03.08' }
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
