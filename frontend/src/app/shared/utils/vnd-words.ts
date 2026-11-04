/** Đọc số tiền VNĐ thành chữ cho hóa đơn (hỗ trợ tới hàng nghìn tỷ). */
const DIGITS = ['không', 'một', 'hai', 'ba', 'bốn', 'năm', 'sáu', 'bảy', 'tám', 'chín'];
const SCALES = ['', 'nghìn', 'triệu', 'tỷ', 'nghìn tỷ', 'triệu tỷ'];

function readGroup(value: number, forceHundred: boolean): string {
  const hundred = Math.floor(value / 100);
  const ten = Math.floor((value % 100) / 10);
  const unit = value % 10;
  const words: string[] = [];

  if (hundred > 0 || forceHundred) {
    words.push(DIGITS[hundred], 'trăm');
  }

  if (ten > 1) {
    words.push(DIGITS[ten], 'mươi');
    if (unit === 1) words.push('mốt');
    else if (unit === 5) words.push('lăm');
    else if (unit > 0) words.push(DIGITS[unit]);
  } else if (ten === 1) {
    words.push('mười');
    if (unit === 5) words.push('lăm');
    else if (unit > 0) words.push(DIGITS[unit]);
  } else if (unit > 0) {
    if (hundred > 0 || forceHundred) words.push('lẻ');
    words.push(DIGITS[unit]);
  }

  return words.join(' ');
}

export function readMoneyInWords(amount: number): string {
  const value = Math.round(Math.abs(amount));
  if (!Number.isFinite(value) || value === 0) {
    return 'Không đồng';
  }

  const groups: number[] = [];
  let remaining = value;
  while (remaining > 0) {
    groups.push(remaining % 1000);
    remaining = Math.floor(remaining / 1000);
  }

  const words: string[] = [];
  for (let index = groups.length - 1; index >= 0; index--) {
    const group = groups[index];
    if (group === 0) continue;

    // Nhóm sau nhóm cao hơn phải đọc đủ "không trăm" (ví dụ 1.000.005).
    const forceHundred = index < groups.length - 1 && group < 100;
    words.push(readGroup(group, forceHundred), SCALES[index] ?? '');
  }

  const text = words.filter(Boolean).join(' ').replace(/\s+/g, ' ').trim();
  return text.charAt(0).toUpperCase() + text.slice(1) + ' đồng chẵn';
}
