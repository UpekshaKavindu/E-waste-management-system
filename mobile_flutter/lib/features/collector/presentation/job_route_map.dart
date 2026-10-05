import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../data/collector_models.dart';

/// OpenStreetMap preview of a job: the pickup pin, the collector's position and the road route
/// between them once [route] has loaded. Until then (or when no route exists) it shows the pins only.
class JobRouteMap extends StatelessWidget {
  const JobRouteMap({super.key, required this.job, required this.route, this.loading = false, this.height = 280});

  final CollectionJob job;
  final JobRoute? route;
  final bool loading;
  final double height;

  @override
  Widget build(BuildContext context) {
    final pickup = _point(route?.pickupLatitude ?? job.pickupLatitude, route?.pickupLongitude ?? job.pickupLongitude);
    final origin = _point(route?.originLatitude, route?.originLongitude);
    final path = [for (final (lat, lng) in route?.points ?? const <(double, double)>[]) LatLng(lat, lng)];

    if (pickup == null) {
      return _frame(
        child: const Center(
          child: Padding(
            padding: EdgeInsets.all(24),
            child: Text('The pickup address could not be placed on the map.',
                textAlign: TextAlign.center, style: AppText.small),
          ),
        ),
      );
    }

    final fitTo = [...path, pickup, if (origin != null) origin];
    return _frame(
      child: Stack(
        children: [
          FlutterMap(
            // Rebuild the camera when the route arrives so it fits the whole path.
            key: ValueKey(path.length + (origin == null ? 0 : 1)),
            options: MapOptions(
              initialCenter: pickup,
              initialZoom: 14,
              initialCameraFit: fitTo.length > 1
                  ? CameraFit.coordinates(coordinates: fitTo, padding: const EdgeInsets.all(48), maxZoom: 16)
                  : null,
              interactionOptions: const InteractionOptions(flags: InteractiveFlag.all & ~InteractiveFlag.rotate),
            ),
            children: [
              TileLayer(
                urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                userAgentPackageName: 'com.example.mobile_flutter',
              ),
              if (path.length > 1)
                PolylineLayer(
                  polylines: [
                    // White casing under the green line keeps it readable over busy map tiles.
                    Polyline(points: path, strokeWidth: 8, color: Colors.white),
                    Polyline(points: path, strokeWidth: 5, color: AppColors.mint600),
                  ],
                ),
              MarkerLayer(
                markers: [
                  if (origin != null)
                    Marker(point: origin, width: 36, height: 36, child: const _OriginMarker()),
                  Marker(
                    point: pickup,
                    width: 40,
                    height: 40,
                    alignment: Alignment.topCenter,
                    child: const Icon(LucideIcons.mapPin, size: 36, color: AppColors.ink900),
                  ),
                ],
              ),
              RichAttributionWidget(
                attributions: [
                  TextSourceAttribution(
                    'OpenStreetMap contributors',
                    onTap: () => launchUrl(Uri.parse('https://openstreetmap.org/copyright')),
                  ),
                ],
              ),
            ],
          ),
          if (loading)
            const Positioned(
              top: 12,
              left: 12,
              child: _Chip(child: Row(mainAxisSize: MainAxisSize.min, children: [
                SizedBox(width: 12, height: 12, child: CircularProgressIndicator(strokeWidth: 2, color: AppColors.mint600)),
                SizedBox(width: 8),
                Text('Finding the route…', style: AppText.small),
              ])),
            ),
        ],
      ),
    );
  }

  Widget _frame({required Widget child}) => ClipRRect(
        borderRadius: BorderRadius.circular(AppRadius.card),
        child: Container(
          height: height,
          decoration: BoxDecoration(
            color: AppColors.mint50,
            borderRadius: BorderRadius.circular(AppRadius.card),
            border: Border.all(color: AppColors.glassBorder),
          ),
          child: child,
        ),
      );

  static LatLng? _point(double? lat, double? lng) => lat == null || lng == null ? null : LatLng(lat, lng);
}

class _OriginMarker extends StatelessWidget {
  const _OriginMarker();

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppColors.mint600.withValues(alpha: 0.18),
        shape: BoxShape.circle,
      ),
      alignment: Alignment.center,
      child: Container(
        width: 24,
        height: 24,
        decoration: BoxDecoration(
          color: AppColors.mint600,
          shape: BoxShape.circle,
          border: Border.all(color: Colors.white, width: 2.5),
        ),
        child: const Icon(LucideIcons.truck, size: 12, color: Colors.white),
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.92),
          borderRadius: BorderRadius.circular(999),
          boxShadow: const [BoxShadow(color: AppColors.glassShadow, blurRadius: 8)],
        ),
        child: child,
      );
}
