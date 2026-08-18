/* Motiva theme experiment — 3 pure color directions.
   Palette derived from the logo: ink #191918, violet #660DE6, teal #0698AA.
   Teal is 3.0:1 on white → non-text only (progress fills, dots). Teal TEXT uses #05707C (4.9:1).
   Violet darkened to #5B18E3 (6.9:1 on white) so it can carry text and links at AA. */

export const THEME_KEYS = ['purple', 'ink', 'dual'];

export const THEME_LABELS = {
  purple: 'Purple-first',
  ink: 'Motiva Ink',
  dual: 'Action / Progress'
};

export const THEME_NOTES = {
  purple: 'סגול הוא צבע המוצר. טורקיז מופיע רק כניצוץ — progress ונקודות.',
  ink: 'שחור, לבן ואפור חם. הסגול והטורקיז הם highlight, והלוגו הוא האלמנט הצבעוני.',
  dual: 'סגול = action. טורקיז = progress. semantic נשאר בנפרד: ענבר ממתין, רוז סיכון.'
};

const SEMANTIC = {
  success: '#0D7A5F', successTint: 'rgba(13,122,95,.08)',
  warn: '#B8710F', warnTint: 'rgba(184,113,15,.10)',
  danger: '#C42D57', dangerTint: 'rgba(196,45,87,.08)',
  info: '#2F6FD0', infoTint: 'rgba(47,111,208,.09)'
};

export const THEMES = {
  purple: {
    ...SEMANTIC,
    bg: '#FAF9F7', panel: '#fff', line: '#E7E4EC', line2: '#F2F1F6',
    ink: '#1A1820', ink3: '#5B5568', ink4: '#8B8698', ink5: '#B7B2C2',
    action: '#5B18E3', actionHover: '#4A11C4', actionTint: 'rgba(91,24,227,.08)',
    progress: '#0698AA', progressText: '#05707C', track: '#EFEDF0',
    r: 16, rBig: 22, rBtn: 12, rInner: 14,
    shadow: '0 6px 18px rgba(30,20,60,.06)', shadowBig: '0 10px 28px rgba(30,20,60,.08)',
    heroBg: 'radial-gradient(circle at 90% 0%,rgba(91,24,227,.11),transparent 56%),linear-gradient(#fff,#FAF9F7)',
    navActiveBg: 'rgba(91,24,227,.07)',
    headTrack: '-.4px', headWeight: 700,
    statusOn: '#0D7A5F', statusAttn: '#B8710F', statusRisk: '#C42D57'
  },
  ink: {
    ...SEMANTIC,
    bg: '#F7F6F3', panel: '#fff', line: '#DCD9D4', line2: '#EDEBE6',
    ink: '#14131A', ink3: '#57545E', ink4: '#7C7972', ink5: '#9A968F',
    action: '#5B18E3', actionHover: '#4A11C4', actionTint: 'rgba(91,24,227,.07)',
    progress: '#0698AA', progressText: '#05707C', track: '#EDEBE6',
    r: 4, rBig: 6, rBtn: 3, rInner: 4,
    shadow: 'none', shadowBig: 'none',
    heroBg: '#fff',
    navActiveBg: 'rgba(91,24,227,.08)',
    headTrack: '-.8px', headWeight: 700,
    statusOn: '#14131A', statusAttn: '#57545E', statusRisk: '#14131A'
  },
  dual: {
    ...SEMANTIC,
    bg: '#FAF9F7', panel: '#fff', line: '#E7E4EC', line2: '#F3F2F6',
    ink: '#1A1820', ink3: '#5B5568', ink4: '#8B8698', ink5: '#B7B2C2',
    action: '#5B18E3', actionHover: '#4A11C4', actionTint: 'rgba(91,24,227,.08)',
    progress: '#0698AA', progressText: '#05707C', track: '#EAF4F4',
    r: 14, rBig: 18, rBtn: 11, rInner: 12,
    shadow: '0 5px 16px rgba(30,20,60,.05)', shadowBig: '0 8px 24px rgba(30,20,60,.07)',
    heroBg: '#fff',
    navActiveBg: 'rgba(91,24,227,.08)',
    headTrack: '-.3px', headWeight: 700,
    statusOn: '#05707C', statusAttn: '#B8710F', statusRisk: '#C42D57'
  }
};

/* Flat style strings the templates consume. One place, so both screens stay identical. */
export function styles(key) {
  const t = THEMES[key] || THEMES.purple;
  const sharp = key === 'ink';
  return {
    t,
    pageStyle: `display:grid;grid-template-columns:248px 1fr;min-height:100vh;background:${t.bg};color:${t.ink}`,
    asideStyle: `border-inline-end:1px solid ${t.line};background:${t.panel};padding:26px 14px 18px;display:flex;flex-direction:column`,
    asideMetaStyle: `color:${t.ink4};font-size:12px;display:block;margin-top:5px`,
    navStyle: `text-decoration:none;display:flex;align-items:center;gap:10px;padding:12px 14px;border-radius:${t.rInner - 4}px;font-size:13.5px;font-weight:600;color:${t.ink3};cursor:pointer`,
    navActiveStyle: `text-decoration:none;display:flex;align-items:center;gap:10px;padding:12px 14px;border-radius:${t.rInner - 4}px;font-size:13.5px;font-weight:700;color:${t.ink};background:${t.navActiveBg};cursor:pointer`,
    navHover: `background:rgba(26,24,32,.04)`,
    badgeStyle: `min-width:16px;height:16px;padding:0 4px;border-radius:${sharp ? '3px' : '8px'};background:${t.action};color:#fff;font-size:10px;font-weight:700;display:flex;align-items:center;justify-content:center`,
    avatarStyle: `width:36px;height:36px;border-radius:${sharp ? '4px' : '50%'};background:${t.bg};border:1px solid ${t.line};color:${t.ink3};display:flex;align-items:center;justify-content:center;font-weight:700;font-size:13px;flex-shrink:0`,

    heroStyle: `position:relative;overflow:hidden;padding:34px 40px 28px;background:${t.heroBg};border-bottom:1px solid ${sharp ? t.ink : t.line}`,
    eyebrowStyle: `font-size:11.5px;font-weight:800;letter-spacing:.09em;color:${t.ink4};margin:0 0 7px`,
    h1Style: `font-size:${sharp ? 30 : 27}px;font-weight:${t.headWeight};letter-spacing:${t.headTrack};display:block;color:${t.ink};line-height:1.15`,
    leadStyle: `font-size:14px;color:${t.ink3};margin:8px 0 22px`,
    sectionLabelStyle: `font-size:11px;font-weight:800;letter-spacing:${sharp ? '.10em' : '.08em'};color:${sharp ? t.ink : t.ink4};margin:0 0 9px`,
    mutedLabelStyle: `font-size:11px;font-weight:800;letter-spacing:${sharp ? '.10em' : '.08em'};color:${t.ink5};margin:20px 0 9px`,
    blockLabelStyle: `font-size:12.5px;font-weight:800;letter-spacing:${sharp ? '.10em' : '.05em'};color:${sharp ? t.ink : t.ink3};margin:0`,
    linkStyle: `font-size:12.5px;font-weight:700;color:${t.action};text-decoration:none`,
    linkHover: `color:${t.actionHover};text-decoration:underline`,

    cardStyle: `background:${t.panel};border:1px solid ${t.line};border-radius:${t.rBig}px;padding:22px 24px;box-shadow:${t.shadowBig}`,
    listCardStyle: `background:${t.panel};border:1px solid ${t.line};border-radius:${t.r}px;overflow:hidden;box-shadow:${t.shadow}`,
    rowStyle: `padding:13px 20px;text-decoration:none;color:inherit;border-top:1px solid ${t.line2};display:flex;align-items:center;justify-content:space-between;gap:16px`,
    rowHover: `background:${t.bg}`,
    rowTitleStyle: `font-size:13.5px;font-weight:600;display:block;color:${t.ink}`,
    rowMetaStyle: `font-size:12px;color:${t.ink4}`,
    emptyStyle: `padding:22px 20px;text-align:center;color:${t.ink4};font-size:13.5px`,

    ctaStyle: `display:inline-block;border:0;background:${t.action};color:#fff;padding:11px 20px;border-radius:${t.rBtn}px;font-weight:700;font-size:13.5px;cursor:pointer;text-decoration:none;flex-shrink:0;white-space:nowrap`,
    ctaHover: `background:${t.actionHover}`,
    chipStyle: `display:inline-flex;align-items:center;gap:8px;padding:7px 13px;border-radius:${sharp ? '3px' : '999px'};border:1px solid ${t.line};background:${t.panel};font-size:12.5px;font-weight:600;color:${t.ink3};text-decoration:none`,
    chipHover: `border-color:${t.action};color:${t.ink}`,

    trackStyle: `height:5px;border-radius:${sharp ? '0' : '999px'};background:${t.track};overflow:hidden`,
    progressText: t.progressText,
    action: t.action,

    switcherStyle: `display:flex;align-items:center;gap:6px;padding:5px;border-radius:${sharp ? '4px' : '999px'};background:${t.bg};border:1px solid ${t.line}`,
    switchOn: `border:0;padding:7px 14px;border-radius:${sharp ? '2px' : '999px'};background:${t.action};color:#fff;font-size:12.5px;font-weight:700;cursor:pointer;font-family:inherit;white-space:nowrap`,
    switchOff: `border:0;padding:7px 14px;border-radius:${sharp ? '2px' : '999px'};background:transparent;color:${t.ink3};font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit;white-space:nowrap`,

    kpiCard: (active, accent) => `display:flex;align-items:center;gap:13px;background:${t.panel};border:1px solid ${active ? accent : t.line};border-radius:${t.rInner}px;padding:16px 18px;cursor:pointer;text-align:start;font-family:inherit;width:100%;box-sizing:border-box;box-shadow:${active ? `inset 0 0 0 1px ${accent}, ${t.shadow}` : t.shadow}`,
    kpiIcon: (color, tint) => `width:36px;height:36px;border-radius:${sharp ? '3px' : '11px'};background:${tint};color:${color};display:flex;align-items:center;justify-content:center;flex-shrink:0`,
    kpiNum: (color) => `font-size:25px;font-weight:800;display:block;color:${color};letter-spacing:-.4px;line-height:1;margin-bottom:3px`,
    kpiLab: `font-size:12.5px;color:${t.ink3};font-weight:600`,
    tag: (color, tint) => `display:inline-flex;align-items:center;gap:6px;font-size:11px;font-weight:700;color:${color};background:${tint};padding:4px 9px;border-radius:${sharp ? '2px' : '999px'};white-space:nowrap;flex-shrink:0`,
    statusText: (color) => `display:flex;align-items:center;gap:6px;font-size:12.5px;font-weight:700;color:${color}`,
    dot: (color) => `width:6px;height:6px;border-radius:50%;background:${color};flex-shrink:0;display:inline-block`
  };
}
