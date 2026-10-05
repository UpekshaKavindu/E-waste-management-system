import 'package:intl/intl.dart';

/// Display helpers — the same conventions as the web app's utils/format.ts.
abstract final class Format {
  static final _money = NumberFormat('#,##0.00', 'en_US');
  static final _weight = NumberFormat('#,##0.##', 'en_US');
  static final _dateTime = DateFormat('MMM d, y, h:mm a');
  static final _date = DateFormat('MMM d, y');

  /// "Rs. 1,234.50" — same currency prefix as the Sales and Processing pages.
  static String money(num amount) => 'Rs. ${_money.format(amount)}';

  static String kg(num kg) => '${_weight.format(kg)} kg';

  /// Signed difference with an explicit +, for weight discrepancies.
  static String signedKg(num kg) => '${kg > 0 ? '+' : ''}${_weight.format(kg)} kg';

  /// ASP.NET sends UTC timestamps without a zone when their Kind is Unspecified. Treat any
  /// zone-less timestamp as UTC so it is not shown in the wrong time.
  static DateTime? parseApiDate(String? iso) {
    if (iso == null || iso.isEmpty) return null;
    final hasZone = RegExp(r'(Z|[+-]\d{2}:?\d{2})$', caseSensitive: false).hasMatch(iso);
    return DateTime.tryParse(hasZone ? iso : '${iso}Z')?.toLocal();
  }

  static String dateTime(String? iso) {
    final d = parseApiDate(iso);
    return d == null ? '—' : _dateTime.format(d);
  }

  static String date(String? iso) {
    final d = parseApiDate(iso);
    return d == null ? '—' : _date.format(d);
  }

  /// Overloads for dates that were already parsed on the way in (see [parseApiDate])
  /// and are therefore local — no need to hand them back to the API as strings.
  static String dateTimeOf(DateTime when) => _dateTime.format(when);

  static String dateOf(DateTime when) => _date.format(when);

  /// "3 days ago", "just now" — for list rows where the exact time is noise.
  static String relative(DateTime when, {DateTime? now}) {
    final difference = (now ?? DateTime.now()).difference(when);
    if (difference.inMinutes < 1) return 'just now';
    if (difference.inHours < 1) return '${difference.inMinutes} min ago';
    if (difference.inDays < 1) return '${difference.inHours} h ago';
    if (difference.inDays < 30) return '${difference.inDays} d ago';
    return _date.format(when);
  }

  /// "NA" from "Nayanathara Amarathunga" — for avatars.
  static String initials(String name) {
    final parts = name.trim().split(RegExp(r'\s+')).where((w) => w.isNotEmpty).toList();
    if (parts.isEmpty) return '?';
    return (parts.first[0] + (parts.length > 1 ? parts.last[0] : '')).toUpperCase();
  }

  /// "3fa85f64…" — enough of a GUID to recognise it without filling the screen.
  static String shortId(String? id) => id == null || id.isEmpty ? '—' : '${id.substring(0, id.length < 8 ? id.length : 8)}…';
}
