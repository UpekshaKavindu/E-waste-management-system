import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_flutter/core/network/api_error.dart';
import 'package:mobile_flutter/core/utils/format.dart';
import 'package:mobile_flutter/features/warehouse/data/processing_enums.dart';
import 'package:mobile_flutter/features/warehouse/data/warehouse_models.dart';

// Pure-logic tests: the rules the app must share with the backend and the web app.

void main() {
  group('enum numbers match the backend declaration order', () {
    test('InventoryStatus', () {
      expect(InventoryStatus.values.map((s) => '${s.apiName}=${s.apiValue}'), [
        'Received=0', 'Sorting=1', 'Dismantling=2', 'Classified=3', 'ReadyForSale=4', 'ExportOnly=5', 'OnHold=6',
        'Recovered=7',
      ]);
    });

    test('ClassificationCategory and source', () {
      expect(ClassificationCategory.values.map((c) => '${c.apiName}=${c.apiValue}'),
          ['Reusable=0', 'LocalRecyclable=1', 'Hazardous=2', 'ExportOnly=3']);
      expect(ClassificationSource.values.map((s) => '${s.apiName}=${s.apiValue}'), ['Manual=0', 'Ai=1']);
    });
  });

  group('status actions', () {
    test('Received can only start sorting', () {
      expect(InventoryStatus.received.manualTransitions(null), [InventoryStatus.sorting]);
    });

    test('A recovered component can only start sorting', () {
      expect(InventoryStatus.recovered.manualTransitions(null), [InventoryStatus.sorting]);
      expect(InventoryStatus.recovered.canDismantle || InventoryStatus.recovered.canClassify, isFalse);
    });

    test('Sorting and Dismantling have no plain status change (dismantle / classify instead)', () {
      expect(InventoryStatus.sorting.manualTransitions(null), isEmpty);
      expect(InventoryStatus.dismantling.manualTransitions(null), isEmpty);
      expect(InventoryStatus.sorting.canDismantle && InventoryStatus.sorting.canClassify, isTrue);
    });

    test('Classified offers only the category outcome plus On hold', () {
      expect(InventoryStatus.classified.manualTransitions(ClassificationCategory.reusable),
          [InventoryStatus.readyForSale, InventoryStatus.onHold]);
      expect(InventoryStatus.classified.manualTransitions(ClassificationCategory.localRecyclable),
          [InventoryStatus.readyForSale, InventoryStatus.onHold]);
      expect(InventoryStatus.classified.manualTransitions(ClassificationCategory.exportOnly),
          [InventoryStatus.exportOnly, InventoryStatus.onHold]);
    });

    test('outcomes are final', () {
      for (final s in [InventoryStatus.readyForSale, InventoryStatus.exportOnly, InventoryStatus.onHold]) {
        expect(s.isTerminal, isTrue);
        expect(s.manualTransitions(ClassificationCategory.reusable), isEmpty);
      }
    });
  });

  group('QR labels', () {
    const id = '3fa85f64-5717-4562-b3fc-2c963f66afa6';

    test('round trip', () => expect(parseInventoryQr(inventoryQrData(id)), id));
    test('bare id, spaces and case are accepted', () => expect(parseInventoryQr('  ewi:${id.toUpperCase()} '), id));
    test('other QR codes are rejected', () {
      expect(parseInventoryQr('https://example.com'), isNull);
      expect(parseInventoryQr('EWI:not-a-guid'), isNull);
      expect(parseInventoryQr(null), isNull);
    });
  });

  test('reserved GeneralCollection type', () {
    expect(isReservedItemType(' generalcollection '), isTrue);
    expect(isReservedItemType('Laptop'), isFalse);
  });

  group('API error messages (same rules as the web apiError.ts)', () {
    DioException withResponse(int status, Object? data) {
      final options = RequestOptions(path: '/x');
      return DioException(requestOptions: options, response: Response(requestOptions: options, statusCode: status, data: data));
    }

    test('ProblemDetails detail wins', () {
      expect(apiErrorMessage(withResponse(409, {'title': 'Conflict', 'detail': 'Job already received.'})), 'Job already received.');
    });

    test('validation errors are listed with readable field names', () {
      final msg = apiErrorMessage(withResponse(400, {
        'errors': {
          'Items[0].WeightKg': ['must be greater than 0'],
        },
      }));
      expect(msg, 'Items 1 › Weight Kg: must be greater than 0');
    });

    test('login message', () {
      expect(apiErrorMessage(withResponse(401, {'message': 'Invalid email or password.'})), 'Invalid email or password.');
    });

    test('5xx shows the title, not the exception text', () {
      expect(apiErrorMessage(withResponse(500, {'title': 'An unexpected error occurred', 'detail': 'NullReference…'})),
          'An unexpected error occurred');
    });

    test('network failure', () {
      final e = DioException(requestOptions: RequestOptions(path: '/x'), type: DioExceptionType.connectionError);
      expect(apiErrorMessage(e), contains('Cannot reach the server'));
    });
  });

  group('formatting', () {
    test('money and weight', () {
      expect(Format.money(1234.5), 'Rs. 1,234.50');
      expect(Format.kg(2.5), '2.5 kg');
      expect(Format.signedKg(0.25), '+0.25 kg');
      expect(Format.signedKg(-1), '-1 kg');
    });

    test('zone-less API timestamps are treated as UTC', () {
      expect(Format.parseApiDate('2026-09-26T10:00:00')!.toUtc(), DateTime.utc(2026, 9, 26, 10));
      expect(Format.parseApiDate('2026-09-26T10:00:00Z')!.toUtc(), DateTime.utc(2026, 9, 26, 10));
    });
  });

  // Shapes copied from the backend DTOs (DeliveryDtos, RecoveredMaterialSummaryDtos,
  // ExtraWasteReceiptQueryDtos); decimals may arrive as ints.
  group('warehouse API responses', () {
    test('a received delivery', () {
      final r = DeliveryResult.fromJson({
        'deliveryId': 'd1',
        'collectorId': 'c1',
        'receivedAt': '2026-09-30T10:00:00',
        'totalPendingAmount': 1250,
        'jobs': [
          {
            'jobId': 'j1', 'verifiedWeightKg': 52.5, 'reportedWeightKg': null, 'discrepancyKg': null,
            'expectedQuantity': 51, 'receivedQuantity': 50, 'paymentId': 'p1', 'paymentAmount': 1250,
            'items': [
              {
                'submissionItemId': 's1', 'itemName': 'Dell laptop', 'expectedQuantity': 50, 'receivedQuantity': 50,
                'inventoryItemId': 'i1', 'itemType': 'Laptop', 'verifiedWeightKg': 52.5,
              },
              {
                'submissionItemId': 's2', 'itemName': 'Router', 'expectedQuantity': 1, 'receivedQuantity': 0,
                'inventoryItemId': null, 'itemType': null, 'verifiedWeightKg': 0,
              },
            ],
          },
        ],
      });
      expect(r.totalPendingAmount, 1250.0);
      final job = r.jobs.single;
      expect(job.reportedWeightKg, isNull);
      expect((job.receivedQuantity, job.expectedQuantity), (50, 51));
      expect(job.items.first.inventoryItemId, 'i1');
      expect(job.items.last.inventoryItemId, isNull); // not brought
    });

    test('a receivable job lists its items with suggestions', () {
      final j = ReceivableJob.fromJson({
        'jobId': 'j1', 'collectorId': 'c1', 'pickupAddress': '1 Main St', 'reportedWeightKg': 30,
        'items': [
          {
            'submissionItemId': 's1', 'itemName': 'Dell laptop', 'quantity': 50, 'expectedWeightKg': 125,
            'suggestedItemType': 'Laptop', 'suggestionSource': 'name',
          },
          {'submissionItemId': 's2', 'itemName': 'Router', 'quantity': 1},
        ],
      });
      expect(j.items.length, 2);
      expect(j.expectedUnits, 51);
      expect(j.items.first.suggestedItemType, 'Laptop');
      expect(j.items.last.suggestedItemType, isNull);
    });

    test('a delivery line sends only what was brought', () {
      final line = const DeliveryJobInput.items(jobId: 'j1', items: [
        DeliveryItemInput(submissionItemId: 's1', receivedQuantity: 50, itemType: 'Laptop', verifiedWeightKg: 52.5),
        DeliveryItemInput(submissionItemId: 's2', receivedQuantity: 0, itemType: 'Battery', verifiedWeightKg: 9),
      ]).toJson();
      final items = line['items'] as List;
      expect(items.last, {'submissionItemId': 's2', 'receivedQuantity': 0, 'itemType': null, 'verifiedWeightKg': 0});
      expect(line.containsKey('verifiedWeightKg'), isFalse);
    });

    test('material stock', () {
      final g = MaterialStockGroup.fromJson({
        'materialType': 'Copper', 'totalWeightKg': 3, 'availableWeightKg': 2.5, 'itemCount': 1,
        'items': [
          {
            'inventoryItemId': 'i1', 'weightKg': 3, 'availableWeightKg': 2.5, 'locationId': 'l1',
            'locationName': 'Ready-for-Sale Storage', 'recordedAt': '2026-09-30T10:00:00',
            'parentInventoryItemId': 'i0', 'parentItemType': 'Laptop',
          },
        ],
      });
      expect(g.reservedWeightKg, 0.5);
      expect(g.items.single.parentItemType, 'Laptop');
    });

    test('a drop-off with nothing accepted has no payment', () {
      final r = ExtraWasteReceiptSummary.fromJson({
        'receiptId': 'r1', 'receivedAt': '2026-09-30T10:00:00', 'collectorId': 'c1', 'collectorName': 'Nimal',
        'itemCount': 1, 'acceptedCount': 0, 'rejectedCount': 1, 'totalWeightKg': 4, 'acceptedWeightKg': 0,
        'paymentId': null, 'paymentStatus': null, 'paymentAmount': null,
      });
      expect(r.paymentStatus, isNull);
      expect(r.acceptedWeightKg, 0.0);
    });
  });
}
