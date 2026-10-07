function headersValid(headers) {
  if (!Array.isArray(headers) || headers.length === 0 || headers.some((key) => typeof key !== 'string' || key.length === 0) || new Set(headers).size !== headers.length) {
    throw new Error('CSV headers must be nonempty, unique strings');
  }
}

export function formatCsv(headers, rows) {
  headersValid(headers);
  const cell = (value) => {
    if (value === null) return '""';
    if (!['string', 'number', 'boolean'].includes(typeof value) || (typeof value === 'number' && !Number.isFinite(value))) throw new Error('CSV cells must be finite scalars or null');
    return `"${String(value).replaceAll('"', '""')}"`;
  };
  const output = [headers.map(cell).join(',')];
  for (const row of rows) {
    if (!row || typeof row !== 'object' || Object.keys(row).length !== headers.length || headers.some((key) => !Object.hasOwn(row, key))) throw new Error('CSV row keys do not match headers');
    output.push(headers.map((key) => cell(row[key])).join(','));
  }
  return `${output.join('\n')}\n`;
}

export function parseCsv(text, expectedHeaders) {
  if (typeof text !== 'string' || text.length === 0) throw new Error('CSV input must include a header');
  const records = [];
  let row = [];
  let index = 0;
  while (index < text.length) {
    if (row.length === 0 && (text[index] === '\n' || text[index] === '\r')) throw new Error('CSV blank physical row');
    let value = '';
    if (text[index] === '"') {
      index++;
      let closed = false;
      while (index < text.length) {
        if (text[index] === '"') {
          if (text[index + 1] === '"') { value += '"'; index += 2; }
          else { index++; closed = true; break; }
        } else { value += text[index++]; }
      }
      if (!closed) throw new Error('CSV unclosed quoted field');
      if (index < text.length && ![',', '\n', '\r'].includes(text[index])) throw new Error('CSV trailing text after quote');
    } else {
      while (index < text.length && ![',', '\n', '\r'].includes(text[index])) {
        if (text[index] === '"') throw new Error('CSV quote inside unquoted field');
        value += text[index++];
      }
    }
    row.push(value);
    if (text[index] === ',') {
      index++;
      if (index === text.length) { row.push(''); records.push(row); row = []; }
    } else {
      if (text[index] === '\r') {
        if (text[index + 1] !== '\n') throw new Error('CSV bare carriage return');
        index += 2;
      } else if (text[index] === '\n') { index++; }
      records.push(row);
      row = [];
    }
  }
  const [headers, ...values] = records;
  headersValid(headers);
  if (expectedHeaders && (headers.length !== expectedHeaders.length || headers.some((header, i) => header !== expectedHeaders[i]))) throw new Error('CSV header schema mismatch');
  return values.map((cells) => {
    if (cells.length !== headers.length) throw new Error('CSV row width mismatch');
    return Object.fromEntries(headers.map((header, i) => [header, cells[i]]));
  });
}
